// The security section of the administrator's dashboard, hidden from a viewer without audit.read: the seven security
// events counted over the last 24 hours and the last 7 days, and the ten latest sensitive changes. What each list
// holds, and why, is in DashboardAuditActions.
//
// Both windows end at the dashboard's one moment of asking and are counted in a single grouped query, so the 24-hour
// figure is always a part of the 7-day one. A row written while the dashboard is being answered is left for the next
// refresh rather than counted by one section and not another.
//
// The counts are made over every kind of actor. A wrong password is a security event whoever typed it.
//
// The sensitive changes are not limited to a window: they are the ten latest, however old, because a change to who
// may do what stays worth knowing about after a quiet week.
//
// It reads the audit table and the accounts, neither of which the row-scope guard watches. It is gated on
// audit.read, which already lets a staff member search every row of that table.
//
// It is resolved from a scope of its own and runs beside the other sections, as GetAdminDashboardHandler
// describes.

namespace MotsSupplierPortal.Infrastructure.Admin.Dashboard;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SecuritySectionHandler(AppDbContext db) : IDashboardSectionHandler<DashboardSecurityDto>
{
    public async Task<DashboardSecurityDto> RunAsync(DashboardRequest request, CancellationToken ct)
    {
        var asOf = request.AsOf;
        var dayStart = asOf.AddHours(-24);
        var weekStart = asOf.AddDays(-7);
        var events = DashboardAuditActions.SecurityEvents;

        var counted = await db.AuditLogs.AsNoTracking()
            .Where(a => events.Contains(a.Action) && a.OccurredAt >= weekStart && a.OccurredAt <= asOf)
            .GroupBy(a => a.Action)
            .Select(g => new { Action = g.Key, Week = g.Count(), Day = g.Count(a => a.OccurredAt >= dayStart) })
            .ToListAsync(ct);

        var byAction = counted.ToDictionary(c => c.Action, StringComparer.Ordinal);
        var counts = events
            .Select(action => byAction.TryGetValue(action, out var c)
                ? new DashboardSecurityEventCountDto(action, c.Day, c.Week)
                : new DashboardSecurityEventCountDto(action, 0, 0))
            .ToList();

        var sensitive = DashboardAuditActions.SensitiveChanges;
        var startedByAPerson = DashboardAuditActions.SensitiveWhenStartedByAPerson;

        var changes = await DashboardAuditRows.LatestAsync(
            db,
            db.AuditLogs.AsNoTracking().Where(a =>
                a.OccurredAt <= asOf
                && (sensitive.Contains(a.Action)
                    || (a.Action == startedByAPerson && a.ActorKind != AuditActorKind.System))),
            ct);

        return new DashboardSecurityDto(counts, changes);
    }
}
