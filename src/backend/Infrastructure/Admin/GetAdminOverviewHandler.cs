// The administrator's overview: users by role, reference-data health, the outbox, the jobs, and audit volume.
//
// Read-only, and every figure is state that already exists and was reachable only by querying the database by
// hand.
//
// Users are counted rather than listed, because the staff listing is its own screen and this is a dashboard.
//
//
// THE REFERENCE-DATA LIST IS HAND-WRITTEN, AND THE TEST READS THE REGISTRY
//
// Which is the right way round. A table added to the registry and not to this screen fails loudly, rather than
// going missing from the one place an administrator checks whether a catalogue is empty.
//
//
// THE INTEGRATION FLAG COMES FROM WHAT IS REGISTERED
//
// Read from the resolved transport rather than from configuration, so it cannot disagree with what is actually
// running.
//
//
// THE JOBS TILE IS THE ONE WORTH HAVING
//
// Switching recurring jobs off silently disables every scheduled transition: submission windows never open or
// close, document expiry is never flagged, the outbox is never drained, awards never reconcile.
//
// Today that is visible only as a single warning line in the startup log, which nobody reads on a running
// system. A missing job is an operational fault, and this is where it becomes visible.
//
// It compares what the scheduler actually has registered against what this application intends, and reads from
// THIS host's own storage rather than the process-wide static facade. The static one is process-wide, and in a
// test process running more than one host the first host wins.

namespace MotsSupplierPortal.Infrastructure.Admin;

using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetAdminOverviewHandler(
    IOutboxTransport transport,
    AppDbContext db, JobStorage jobStorage, IConfiguration configuration)
    : IGetAdminOverviewHandler
{
    public async Task<AdminOverviewDto> HandleAsync(CancellationToken ct)
    {
        var usersByRole = await db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>()
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var roleNames = await db.Set<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>()
            .AsNoTracking().Select(r => new { r.Id, r.Name }).ToListAsync(ct);

        var byRole = usersByRole
            .Select(u => new AdminCountDto(
                roleNames.FirstOrDefault(r => r.Id == u.Key)?.Name ?? "(unknown)", u.Count))
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .ToList();

        var referenceData = new List<ReferenceTableHealthDto>
        {
            await HealthAsync(ReferenceTables.Categories, db.Set<Category>().Select(c => c.IsActive), ct),
            await HealthAsync(ReferenceTables.DocumentTypes, db.Set<DocumentType>().Select(d => d.IsActive), ct),
            await HealthAsync(ReferenceTables.Currencies, db.Set<Currency>().Select(c => c.IsActive), ct),
            await HealthAsync(ReferenceTables.UnitsOfMeasure, db.Set<UnitOfMeasure>().Select(u => u.IsActive), ct),
            await HealthAsync(ReferenceTables.Regions, db.Set<Region>().Select(r => r.IsActive), ct),
            await HealthAsync(ReferenceTables.Incoterms, db.Set<Incoterm>().Select(i => i.IsActive), ct),
        };

        var pending = await db.OutboxMessages.CountAsync(m => m.SyncStatus == OutboxSyncStatus.Pending, ct);
        var failed = await db.OutboxMessages.CountAsync(m => m.SyncStatus == OutboxSyncStatus.Failed, ct);

        var oldestPending = await db.OutboxMessages
            .Where(m => m.SyncStatus == OutboxSyncStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Select(m => (DateTimeOffset?)m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var auditRows = await db.AuditLogs
            .CountAsync(a => a.OccurredAt >= DateTimeOffset.UtcNow.AddHours(-24), ct);

        return new AdminOverviewDto(
            byRole,
            roleNames.Count,
            referenceData,
            new OutboxHealthDto(
                pending, failed,
                oldestPending is { } oldest
                    ? (int)Math.Max(0, (DateTimeOffset.UtcNow - oldest).TotalMinutes)
                    : null,
                ErpTransportConfigured: transport is not LoggingOutboxTransport),
            JobHealth(),
            auditRows);
    }

    private static async Task<ReferenceTableHealthDto> HealthAsync(
        string table, IQueryable<bool> activeFlags, CancellationToken ct)
    {
        var flags = await activeFlags.ToListAsync(ct);
        return new ReferenceTableHealthDto(table, flags.Count(f => f), flags.Count(f => !f));
    }

    private JobHealthDto JobHealth()
    {
        var enabled = configuration.GetValue("Jobs:EnableRecurring", defaultValue: true);

        var registered = jobStorage.GetConnection().GetRecurringJobs()
            .Select(j => j.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var missing = RecurringJobs.All
            .Where(expected => !registered.Contains(expected))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return new JobHealthDto(enabled, RecurringJobs.All, registered, missing);
    }
}
