// The recent activity section of the administrator's dashboard, hidden from a viewer without audit.read: how many
// rows people and other systems wrote in the last 24 hours, the ten newest of them, and how many the system wrote on
// its own in the same 24 hours. What is left out, and why, is in DashboardRecentActivityDto and DashboardAuditActions.
//
// The count and the list are built on one filter, so the rows shown are the same kind of row as the rows counted.
// The list is not limited to the 24 hours: after a quiet day it reaches back past them, and a quiet day is when
// knowing what happened last matters most. The session rows SessionAuditActions names are left out of the system's
// count as well as the feed's, which is the rule every administrator's count of this table keeps.
//
// Everything is measured up to the dashboard's one moment of asking, like every other window on it.
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

public sealed class RecentActivitySectionHandler(AppDbContext db) : IDashboardSectionHandler<DashboardRecentActivityDto>
{
    public async Task<DashboardRecentActivityDto> RunAsync(DashboardRequest request, CancellationToken ct)
    {
        var asOf = request.AsOf;
        var dayStart = asOf.AddHours(-24);
        var sessions = SessionAuditActions.All;
        var leftOut = DashboardAuditActions.LeftOutOfTheFeed.ToArray();

        var activity = db.AuditLogs.AsNoTracking()
            .Where(a => a.OccurredAt <= asOf && !sessions.Contains(a.Action));

        var feed = activity.Where(a => a.ActorKind != AuditActorKind.System && !leftOut.Contains(a.Action));

        var last24Hours = await feed.CountAsync(a => a.OccurredAt >= dayStart, ct);

        var systemLast24Hours = await activity
            .CountAsync(a => a.ActorKind == AuditActorKind.System && a.OccurredAt >= dayStart, ct);

        var latest = await DashboardAuditRows.LatestAsync(db, feed, ct);

        return new DashboardRecentActivityDto(last24Hours, systemLast24Hours, latest);
    }
}
