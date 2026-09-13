using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Infrastructure.Admin;

/// <summary>
/// SCR-723's read: every award's ERP synchronisation state, and whether a real transport exists to do it.
///
/// <para><b>The transport flag is not decoration.</b> EPIC-23's adapter has not landed, so what is
/// registered is a logging stand-in - it accepts everything and sends nothing. A monitor showing a column
/// of Synced without saying that would be an instrument asserting something untrue, which is the failure
/// B-1/BRULE-011 recorded on the dashboard tile and the same one avoided here.</para>
///
/// <para>Failed first, then oldest award: the order an operator works in. Retrying is not offered by this
/// handler - `POST /awards/{code}/retry-erp-sync` already exists behind integration.retry and enforces
/// §6.1's guard that only a Failed sync retries. A second retry path would be a second guard to keep in
/// step.</para>
/// </summary>
public sealed class GetErpSyncMonitorHandler(AppDbContext db, IOutboxTransport transport) : IGetErpSyncMonitorHandler
{
    private const int PageSize = 100;

    public async Task<ErpSyncMonitorDto> HandleAsync(string? status, CancellationToken ct)
    {
        var counts = Enum.GetValues<ErpSyncStatus>().ToDictionary(s => s.ToString(), _ => 0);
        var grouped = await db.Awards.AsNoTracking()
            .GroupBy(a => a.ErpSyncStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        foreach (var group in grouped) counts[group.Status.ToString()] = group.Count;

        var wanted = (status ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => Enum.TryParse<ErpSyncStatus>(token, ignoreCase: false, out var parsed) ? parsed : (ErpSyncStatus?)null)
            .Where(parsed => parsed is not null)
            .Select(parsed => parsed!.Value)
            .ToList();

        var query = db.Awards.AsNoTracking().AsQueryable();
        if (wanted.Count > 0) query = query.Where(a => wanted.Contains(a.ErpSyncStatus));

        // Joined to the RFQ because an award reference alone does not tell an operator WHICH tender is
        // stuck, and that is the first thing they will be asked.
        var awards = await query
            .OrderBy(a => a.ErpSyncStatus == ErpSyncStatus.Failed ? 0 : 1)
            .ThenBy(a => a.CreatedAt)
            .Take(PageSize)
            .Join(db.Rfqs.AsNoTracking(), a => a.RfqId, r => r.Id, (a, r) => new ErpSyncRowDto(
                r.ReferenceCode, a.ErpSyncStatus.ToString(),
                a.ErpRetryCount, a.ErpSyncedAt, a.ExternalPurchaseOrderRef))
            .ToListAsync(ct);

        return new ErpSyncMonitorDto(transport is not LoggingOutboxTransport, counts, awards);
    }
}
