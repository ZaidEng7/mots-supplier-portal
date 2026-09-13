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
/// SCR-722's read: the outbox, per message, with the payload an operator needs to judge a replay.
///
/// <para>The payload is integration data - what was already sent to an external system - and the
/// endpoint is system_admin only. It is returned whole rather than summarised because a message that
/// cannot be read cannot be judged, and the alternative is an operator replaying blind.</para>
/// </summary>
public sealed class GetOutboxMonitorHandler(AppDbContext db) : IGetOutboxMonitorHandler
{
    private const int PageSize = 100;

    public async Task<OutboxMonitorDto> HandleAsync(string? status, CancellationToken ct)
    {
        // Every status, including the ones with no rows. An operator who cannot see "Failed: 0" cannot
        // tell it from a count that failed to load.
        var counts = Enum.GetValues<OutboxSyncStatus>().ToDictionary(s => s.ToString(), _ => 0);
        var grouped = await db.OutboxMessages.AsNoTracking()
            .GroupBy(m => m.SyncStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        foreach (var group in grouped) counts[group.Status.ToString()] = group.Count;

        // §6.2's multi-value OR form, so ?status=Failed,Pending is "everything not yet delivered" -
        // the question an operator actually has. Parsed the same way the endpoint validated it; an
        // unrecognised token never reaches here, because the endpoint answers 422 first.
        var wanted = (status ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => Enum.TryParse<OutboxSyncStatus>(token, ignoreCase: false, out var parsed) ? parsed : (OutboxSyncStatus?)null)
            .Where(parsed => parsed is not null)
            .Select(parsed => parsed!.Value)
            .ToList();

        var query = db.OutboxMessages.AsNoTracking().AsQueryable();
        if (wanted.Count > 0)
        {
            query = query.Where(m => wanted.Contains(m.SyncStatus));
        }

        // Failed first, then oldest: the order an operator works in. A newest-first list buries the
        // message that has been stuck longest, which is the one that matters.
        var messages = await query
            .OrderBy(m => m.SyncStatus == OutboxSyncStatus.Failed ? 0 : 1)
            .ThenBy(m => m.CreatedAt)
            .Take(PageSize)
            .Select(m => new OutboxMessageRowDto(
                m.Id, m.Type, m.SyncStatus.ToString(), m.CreatedAt, m.ProcessedAt, m.PayloadJson))
            .ToListAsync(ct);

        return new OutboxMonitorDto(counts, messages);
    }
}
