using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Admin;

/// <summary>
/// T-062/FR-DSH-006/SCR-700. Read-only, and every figure is state that already exists and is
/// currently reachable only by querying Postgres by hand.
/// </summary>
public sealed class GetAdminOverviewHandler(
    AppDbContext db, JobStorage jobStorage, IConfiguration configuration)
    : IGetAdminOverviewHandler
{
    public async Task<AdminOverviewDto> HandleAsync(CancellationToken ct)
    {
        // Users by role, from Identity's own join table. Counted rather than listed: SCR-701 is the
        // user LISTING screen and this is the dashboard.
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
                    : null),
            JobHealth(),
            auditRows);
    }

    private static async Task<ReferenceTableHealthDto> HealthAsync(
        string table, IQueryable<bool> activeFlags, CancellationToken ct)
    {
        var flags = await activeFlags.ToListAsync(ct);
        return new ReferenceTableHealthDto(table, flags.Count(f => f), flags.Count(f => !f));
    }

    /// <summary>
    /// What Hangfire actually has registered, against what this application intends.
    ///
    /// <para><b>This is the tile worth having.</b> <c>Jobs:EnableRecurring=false</c> silently disables
    /// every scheduled transition - submission windows never open or close, document expiry is never
    /// flagged, the outbox is never drained, awards never reconcile - and today that is visible only as
    /// a single warning line in the startup log, which nobody reads on a running system. A missing job
    /// is an operational fault and this is where it becomes visible.</para>
    /// </summary>
    private JobHealthDto JobHealth()
    {
        var enabled = configuration.GetValue("Jobs:EnableRecurring", defaultValue: true);

        // Read from THIS host's storage rather than the static JobStorage.Current facade, for the same
        // reason Program.cs resolves IRecurringJobManager from DI: the static one is process-wide and
        // in a test process running more than one host the first host wins.
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
