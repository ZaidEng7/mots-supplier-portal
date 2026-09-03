using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Notifications;

/// <summary>
/// Resolves the recipient groups BUSINESS-PROCESSES.md's transition tables name.
///
/// <para><b>Recipients come from the tables, not from inference.</b> "Notify the committee" and
/// "notify the officer" are different sets, and the table says which for every transition - so each
/// method here corresponds to a phrase in that column rather than to a convenient query.</para>
///
/// <para><b>Two of them are pools rather than individuals, and that is a known gap, not a
/// choice.</b> Nothing in the domain records WHICH officer owns an RFQ (there is no
/// CreatedByUserId), and nothing resolves a single named approver from the AwardApprove role claim.
/// Both notify the whole role-and-organization pool - the same answer
/// ReviewApplicationHandlers.GetReviewerPoolUserIdsAsync already gives for onboarding review, for
/// the same reason. Reported as an open business question rather than resolved by invention.</para>
/// </summary>
public static class NotificationRecipients
{
    private static IQueryable<Guid> InRole(AppDbContext db, string role) =>
        from ur in db.UserRoles
        join r in db.Roles on ur.RoleId equals r.Id
        join u in db.Users on ur.UserId equals u.Id
        where r.Name == role
        select u.Id;

    private static IQueryable<Guid> InRoleForOrganization(AppDbContext db, string role, Guid organizationId) =>
        from ur in db.UserRoles
        join r in db.Roles on ur.RoleId equals r.Id
        join u in db.Users on ur.UserId equals u.Id
        where r.Name == role && u.OrganizationId == organizationId
        select u.Id;

    /// <summary>§3.1 "In-app to `procurement_manager`".</summary>
    public static Task<List<Guid>> ProcurementManagersAsync(AppDbContext db, Guid organizationId, CancellationToken ct) =>
        InRoleForOrganization(db, Roles.ProcurementManager, organizationId).Distinct().ToListAsync(ct);

    /// <summary>§3.1 "In-app to officer" - the pool, see the class note.</summary>
    public static Task<List<Guid>> ProcurementOfficersAsync(AppDbContext db, Guid organizationId, CancellationToken ct) =>
        InRoleForOrganization(db, Roles.ProcurementOfficer, organizationId).Distinct().ToListAsync(ct);

    /// <summary>
    /// §3.1 "committee" - the officers and managers of the RFQ's own organization.
    ///
    /// <para>No Committee entity exists; the tables use the word for the internal group that runs an
    /// RFQ, which in this domain is exactly those two roles scoped to the organization. Named here
    /// so the interpretation is in one place rather than repeated at each call site.</para>
    /// </summary>
    public static async Task<List<Guid>> CommitteeAsync(AppDbContext db, Guid organizationId, CancellationToken ct)
    {
        var officers = await ProcurementOfficersAsync(db, organizationId, ct);
        var managers = await ProcurementManagersAsync(db, organizationId, ct);
        return [.. officers.Concat(managers).Distinct()];
    }

    /// <summary>§3.4 "in-app + email to approver(s)". The pool - see the class note.</summary>
    public static Task<List<Guid>> AwardApproversAsync(AppDbContext db, Guid organizationId, CancellationToken ct) =>
        InRoleForOrganization(db, Roles.ProcurementManager, organizationId).Distinct().ToListAsync(ct);

    /// <summary>§3.4 "Alert to `system_admin`". Not organization-scoped: an ERP failure is platform-level.</summary>
    public static Task<List<Guid>> SystemAdminsAsync(AppDbContext db, CancellationToken ct) =>
        InRole(db, Roles.SystemAdmin).Distinct().ToListAsync(ct);

    /// <summary>§3.1 "In-app to invitees" - every user of every supplier invited to this RFQ.</summary>
    public static Task<List<Guid>> RfqInviteeUsersAsync(AppDbContext db, Guid rfqId, CancellationToken ct) =>
        (from i in db.Invitations
         join u in db.Users on i.SupplierId equals u.SupplierId
         where i.RfqId == rfqId
         select u.Id).Distinct().ToListAsync(ct);

    /// <summary>§3.3 "In-app to evaluators" - those assigned and not recused.</summary>
    public static Task<List<Guid>> AssignedEvaluatorsAsync(AppDbContext db, Guid evaluationId, CancellationToken ct) =>
        db.EvaluationAssignments
            .Where(a => a.EvaluationId == evaluationId && a.RecusedAt == null)
            .Select(a => a.EvaluatorUserId)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>Every user belonging to one supplier - §3.2's "in-app to supplier".</summary>
    public static Task<List<Guid>> SupplierUsersAsync(AppDbContext db, Guid supplierId, CancellationToken ct) =>
        db.Users.Where(u => u.SupplierId == supplierId).Select(u => u.Id).Distinct().ToListAsync(ct);
}
