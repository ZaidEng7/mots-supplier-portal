// Two reads the administrator's overview and the dashboard's system health section both report: the health of
// each reference table, and the state of the outbox.
//
// They are written once so the two screens cannot disagree while both exist. The overview came first, and these
// are its queries, moved here unchanged.
//
// The reference tables are hand-written, one line each, and the overview's test reads the registry of tables, so
// a table added to the registry and not to this list fails loudly rather than going missing from the one place an
// administrator checks whether a catalogue is empty.
//
// The oldest pending message is a time rather than an age, so each caller measures it from its own instant.

namespace MotsSupplierPortal.Infrastructure.Admin;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed record OutboxState(int Pending, int Failed, DateTimeOffset? OldestPendingAt);

public static class OperationalHealthReads
{
    public static async Task<IReadOnlyList<ReferenceTableHealthDto>> ReferenceTablesAsync(
        AppDbContext db, CancellationToken ct) =>
    [
        await HealthAsync(ReferenceTables.Categories, db.Set<Category>().Select(c => c.IsActive), ct),
        await HealthAsync(ReferenceTables.DocumentTypes, db.Set<DocumentType>().Select(d => d.IsActive), ct),
        await HealthAsync(ReferenceTables.Currencies, db.Set<Currency>().Select(c => c.IsActive), ct),
        await HealthAsync(ReferenceTables.UnitsOfMeasure, db.Set<UnitOfMeasure>().Select(u => u.IsActive), ct),
        await HealthAsync(ReferenceTables.Regions, db.Set<Region>().Select(r => r.IsActive), ct),
        await HealthAsync(ReferenceTables.Incoterms, db.Set<Incoterm>().Select(i => i.IsActive), ct),
    ];

    public static async Task<OutboxState> OutboxAsync(AppDbContext db, CancellationToken ct)
    {
        var pending = await db.OutboxMessages.CountAsync(m => m.SyncStatus == OutboxSyncStatus.Pending, ct);
        var failed = await db.OutboxMessages.CountAsync(m => m.SyncStatus == OutboxSyncStatus.Failed, ct);

        var oldestPending = await db.OutboxMessages
            .Where(m => m.SyncStatus == OutboxSyncStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Select(m => (DateTimeOffset?)m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return new OutboxState(pending, failed, oldestPending);
    }

    private static async Task<ReferenceTableHealthDto> HealthAsync(
        string table, IQueryable<bool> activeFlags, CancellationToken ct)
    {
        var flags = await activeFlags.ToListAsync(ct);
        return new ReferenceTableHealthDto(table, flags.Count(f => f), flags.Count(f => !f));
    }
}
