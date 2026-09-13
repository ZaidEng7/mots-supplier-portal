// The outbox monitor: every message, with the payload an operator needs in order to judge a replay.
//
// The payload is integration data, which is what was already sent to an external system, and the endpoint is
// administrator-only. It is returned whole rather than summarised, because a message that cannot be read cannot
// be judged and the alternative is an operator replaying blind.
//
// Every status appears in the counts, including the ones with no rows. An operator who cannot see a zero cannot
// tell it from a count that failed to load.
//
// Several statuses may be requested at once, so asking for failed and pending together is "everything not yet
// delivered", which is the question an operator actually has. An unrecognised value never reaches here, because
// the endpoint refuses it first.
//
// Failed first, then oldest, which is the order an operator works in. A newest-first list buries the message that
// has been stuck longest, which is the one that matters.

namespace MotsSupplierPortal.Infrastructure.Admin;

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

public sealed class GetOutboxMonitorHandler(AppDbContext db) : IGetOutboxMonitorHandler
{
    private const int PageSize = 100;

    public async Task<OutboxMonitorDto> HandleAsync(string? status, CancellationToken ct)
    {
        var counts = Enum.GetValues<OutboxSyncStatus>().ToDictionary(s => s.ToString(), _ => 0);
        var grouped = await db.OutboxMessages.AsNoTracking()
            .GroupBy(m => m.SyncStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        foreach (var group in grouped) counts[group.Status.ToString()] = group.Count;

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
