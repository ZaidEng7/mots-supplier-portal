// The latest audit rows of a query, as the administrator's dashboard shows them: newest first, each with the name
// of whoever wrote it. The security section's sensitive changes and the recent activity feed both read through here,
// so the two lists cannot name the same actor two different ways.
//
// The name comes from a left join to the accounts, made after the ten rows are chosen, so the join touches ten rows
// and not the whole table. An account that still exists gives its current full name. A row whose account is gone,
// or that never named one, falls back to the label it was written with, which is how an integration's key name and
// the hourly sync's "system" are stored. A row with neither says "system", because the only writer that leaves both
// empty is the product acting on its own.
//
// The order is decided by the database, by time and then by row id, rather than re-sorted here. Two rows written in
// the same instant then come back in the same order on every refresh, and in the same order the audit search shows.

namespace MotsSupplierPortal.Infrastructure.Admin.Dashboard;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class DashboardAuditRows
{
    public const int Shown = 10;

    public static async Task<IReadOnlyList<DashboardAuditRowDto>> LatestAsync(
        AppDbContext db, IQueryable<AuditLog> rows, CancellationToken ct)
    {
        var latest = rows
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Take(Shown);

        return await (
                from a in latest
                join u in db.Users.AsNoTracking() on a.ActorUserId equals (Guid?)u.Id into actors
                from u in actors.DefaultIfEmpty()
                orderby a.OccurredAt descending, a.Id descending
                select new DashboardAuditRowDto(
                    a.Id,
                    a.OccurredAt,
                    a.Action,
                    a.ActorKind,
                    u != null ? u.FullName : a.ActorLabel ?? DashboardAuditActions.SystemActorName,
                    a.AggregateType,
                    a.ReferenceCode))
            .ToListAsync(ct);
    }
}
