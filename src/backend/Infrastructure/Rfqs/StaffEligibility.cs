// Can this member of staff be handed this responsibility?
//
//
// THE QUESTION IS ABOUT THE PERMISSION, NOT THE ROLE NAME
//
// A ministry that moves tender approval to another role keeps a correct answer. That is the same reason the
// procurement dashboard decides its approvals card from the permission rather than from a role name.
//
// Asking for a role name would make ownership silently wrong the first time a ministry rearranged its roles.
//
//
// AND IT IS READ FROM THE LIVE GRANT, NOT THE SEEDED MAP
//
// The static default map is the seed, and roles are administrator-editable afterwards, so the map and the
// live grant diverge the moment somebody edits a role.
//
// This check would then refuse a nomination the caller's own gate would have allowed, or allow one it would
// refuse. The permission resolver reads the stored claims for exactly this reason, and this query is the
// same question asked in the database.
//
//
// THREE CONDITIONS, ALL OF WHICH MATTER
//
// The user must belong to the tender's own organization, because ownership must not cross an organization
// boundary. They must be active, because a deactivated account cannot read the notification the ownership
// entitles it to. And they must hold the permission.
//
// Dropping any one of the three admits an assignment that looks recorded and does nothing.
//
// The list form and the single-user form ask the same three conditions, so what a picker OFFERS and what
// the write ACCEPTS cannot disagree. A picker built from a different query is exactly that failure.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class StaffEligibility
{
    public static Task<List<Domain.Identity.AppUser>> HoldersAsync(
        AppDbContext db, Guid organizationId, string permission, CancellationToken ct) =>
        (from u in db.Users
         join ur in db.UserRoles on u.Id equals ur.UserId
         join rc in db.RoleClaims on ur.RoleId equals rc.RoleId
         where u.OrganizationId == organizationId && u.IsActive
               && rc.ClaimType == "perms" && rc.ClaimValue == permission
         select u).Distinct().OrderBy(u => u.FullName).ToListAsync(ct);

    public static Task<bool> HoldsPermissionAsync(
        AppDbContext db, Guid userId, Guid organizationId, string permission, CancellationToken ct) =>
        (from u in db.Users
         join ur in db.UserRoles on u.Id equals ur.UserId
         join rc in db.RoleClaims on ur.RoleId equals rc.RoleId
         where u.Id == userId && u.OrganizationId == organizationId && u.IsActive
               && rc.ClaimType == "perms" && rc.ClaimValue == permission
         select u.Id).AnyAsync(ct);
}
