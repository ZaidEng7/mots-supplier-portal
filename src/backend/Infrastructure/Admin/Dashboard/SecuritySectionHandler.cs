// The security section of the administrator's dashboard, hidden from a viewer without audit.read: the seven security
// events counted over the last 24 hours and the last 7 days, the day those counts start from, whether each event is
// spiking, and the ten latest sensitive changes. What each list holds, and why, is in DashboardAuditActions; what the
// figures mean, and how far back a zero reaches, is in DashboardSecurityDto.
//
// Both windows end at the dashboard's one moment of asking and are counted in a single grouped query, so the 24-hour
// figure is always a part of the 7-day one. The same query counts, for the spike rule, each event's rows over the
// days before the last 24 hours that fall on or after the day the counts start from, as DashboardSecuritySpike
// describes; the displayed 7-day figure keeps every row of the week. A row written while the dashboard is being answered is left for the next
// refresh rather than counted by one section and not another.
//
// The counts are made over every kind of actor. A wrong password is a security event whoever typed it.
//
// The day the counts start from is read as the oldest row of each sign-in action, one index lookup each on the
// action and time index, joined into one query. Asking for the oldest row across all five at once would read every
// sign-in row the table holds, and that table gains a row for every sign-in, forever.
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
        var events = DashboardAuditActions.SecurityEvents.ToArray();

        var countedSince = await CountedSinceAsync(ct);
        var baselineStart = DashboardSecuritySpike.BaselineStart(countedSince, asOf);

        var counted = await db.AuditLogs.AsNoTracking()
            .Where(a => events.Contains(a.Action) && a.OccurredAt >= weekStart && a.OccurredAt <= asOf)
            .GroupBy(a => a.Action)
            .Select(g => new
            {
                Action = g.Key,
                Week = g.Count(),
                Day = g.Count(a => a.OccurredAt >= dayStart),
                Baseline = g.Count(a => a.OccurredAt >= baselineStart && a.OccurredAt < dayStart),
            })
            .ToListAsync(ct);

        var byAction = counted.ToDictionary(c => c.Action, StringComparer.Ordinal);
        var counts = events
            .Select(action =>
            {
                var (day, week, baseline) =
                    byAction.TryGetValue(action, out var c) ? (c.Day, c.Week, c.Baseline) : (0, 0, 0);
                return new DashboardSecurityEventCountDto(
                    action, day, week, DashboardSecuritySpike.IsSpiking(day, baseline, countedSince, asOf));
            })
            .ToList();

        var sensitive = DashboardAuditActions.SensitiveChanges.ToArray();
        var startedByAPerson = DashboardAuditActions.SensitiveWhenStartedByAPerson;

        var changes = await DashboardAuditRows.LatestAsync(
            db,
            db.AuditLogs.AsNoTracking().Where(a =>
                a.OccurredAt <= asOf
                && (sensitive.Contains(a.Action)
                    || (a.Action == startedByAPerson && a.ActorKind != AuditActorKind.System))),
            ct);

        return new DashboardSecurityDto(countedSince, counts, changes);
    }

    private async Task<DateTimeOffset?> CountedSinceAsync(CancellationToken ct)
    {
        IQueryable<DateTimeOffset>? oldest = null;

        foreach (var action in DashboardAuditActions.SignInAttempts)
        {
            var first = db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == action)
                .OrderBy(a => a.OccurredAt)
                .Select(a => a.OccurredAt)
                .Take(1);

            oldest = oldest is null ? first : oldest.Concat(first);
        }

        var firsts = await oldest!.ToListAsync(ct);
        return firsts.Count == 0 ? null : firsts.Min();
    }
}
