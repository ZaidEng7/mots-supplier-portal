using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>
/// A-7: can this staff user be handed this responsibility?
///
/// <para><b>Asked of the PERMISSION, not of the role name.</b> A tenant that moves RFQ approval to
/// another role keeps a correct answer, which is the same reason <c>ProcurementDashboardHandler</c>
/// decides its Approvals card from <c>rfq.approve</c> rather than from <c>procurement_manager</c>.
/// Asking for the role name would make ownership silently wrong the first time a ministry
/// rearranges its roles.</para>
///
/// <para><b>Read from the role's DB-stored <c>perms</c> claims, not from
/// <c>Roles.DefaultPermissions</c>.</b> That dictionary is the SEED (RoleSeeder) and roles are
/// admin-editable thereafter (FR-ADM-002), so the static map and the live grant diverge the moment an
/// administrator edits a role - and this check would then refuse a nomination the caller's own gate
/// would have allowed, or allow one it would refuse. <c>PermissionResolver</c> reads the claims for
/// exactly this reason, and this query is the same question asked in SQL.</para>
///
/// <para><b>Three conditions, all of which matter.</b> The user must belong to the RFQ's own
/// organization (BRULE-029 - ownership must not cross an organization boundary), must be active (a
/// deactivated account cannot read the notification the ownership entitles it to), and must hold the
/// permission. Dropping any one admits an assignment that looks recorded and does nothing.</para>
/// </summary>
internal static class StaffEligibility
{
    /// <summary>
    /// Everyone in this organization who holds this permission and could be handed work.
    ///
    /// <para>The same three conditions as <see cref="HoldsPermissionAsync"/>, expressed as a list
    /// rather than as a predicate - so what a screen OFFERS and what the write ACCEPTS cannot
    /// disagree, which is the failure mode of a picker built from a different query.</para>
    /// </summary>
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
