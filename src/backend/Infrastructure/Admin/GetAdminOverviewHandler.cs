// The administrator's overview: users by role, reference-data health, the outbox, the jobs, and audit volume.
//
// Read-only, and every figure is state that already exists and was reachable only by querying the database by
// hand.
//
// Users are counted rather than listed, because the staff listing is its own screen and this is a dashboard.
//
// The audit figure is the last 24 hours of activity on the registry, and it leaves out the session rows that
// SessionAuditActions names. Every sign-in, refresh-token reuse detection and sign-out is recorded, and counted
// in they would make the figure follow office hours rather than the work. The audit search still shows them.
//
//
// THE REFERENCE-DATA LIST IS HAND-WRITTEN, AND THE TEST READS THE REGISTRY
//
// Which is the right way round. A table added to the registry and not to this screen fails loudly, rather than
// going missing from the one place an administrator checks whether a catalogue is empty.
//
// The list and the outbox figures are read through OperationalHealthReads, which the dashboard's system health
// section reads too, so the two screens cannot disagree while both exist.
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
using MotsSupplierPortal.Domain.Audit;
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

        var referenceData = await OperationalHealthReads.ReferenceTablesAsync(db, ct);

        var (pending, failed, oldestPending) = await OperationalHealthReads.OutboxAsync(db, ct);

        var auditRows = await db.AuditLogs
            .Where(a => !SessionAuditActions.All.Contains(a.Action))
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
