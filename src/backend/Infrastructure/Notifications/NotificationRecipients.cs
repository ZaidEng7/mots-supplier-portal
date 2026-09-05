using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Notifications;

/// <summary>
/// Resolves the recipient groups BUSINESS-PROCESSES.md's transition tables name.
///
/// <para><b>Recipients come from the tables, not from inference.</b> "Notify the committee" and
/// "notify the officer" are different sets, and the table says which for every transition - so each
/// method here corresponds to a phrase in that column rather than to a convenient query.</para>
///
/// <para><b>A-7 closed the officer half of what used to be a known gap here.</b> "Notify the officer"
/// resolved to the whole role-and-organization pool, because nothing recorded which officer owned an
/// RFQ. <c>Rfq.OwnerUserId</c> now does, and <see cref="RfqOwnerAsync"/> resolves the phrase to that
/// person - falling back to the pool only when the RFQ predates ownership or its owner is no longer
/// an active user, because an RFQ nobody is told about is worse than one the pool is told about.</para>
///
/// <para><b>Award approval is still a pool, deliberately.</b> <see cref="AwardApproversAsync"/> is
/// §3.4's "approver(s)" on the AWARD chain, and which manager an award routes to is BRULE-072/074's
/// amount-threshold question - undecided (OQ-004, T-075). Naming one here would invent the routing
/// rule rather than record a decision, so it stays the pool and stays reported.</para>
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

    /// <summary>§3.1 "In-app to officer" as a POOL. Kept for the fallback below and for callers that
    /// genuinely mean every officer; a rule about "the officer" wants <see cref="RfqOwnerAsync"/>.</summary>
    public static Task<List<Guid>> ProcurementOfficersAsync(AppDbContext db, Guid organizationId, CancellationToken ct) =>
        InRoleForOrganization(db, Roles.ProcurementOfficer, organizationId).Distinct().ToListAsync(ct);

    /// <summary>
    /// A-7: §3.1's "the officer" as a PERSON - the officer who owns this RFQ.
    ///
    /// <para><b>Two fallbacks to the pool, both deliberate.</b> An RFQ created before A-7 has no
    /// owner, and an owner whose account has since been deactivated cannot read a notification. In
    /// either case the alternative to the pool is nobody, and a transition in a live tender that
    /// notifies nobody is the failure this whole ruling exists to prevent - so the fallback is
    /// wider-than-ideal on purpose rather than silent.</para>
    /// </summary>
    public static async Task<List<Guid>> RfqOwnerAsync(AppDbContext db, Rfq rfq, CancellationToken ct)
    {
        if (rfq.OwnerUserId is { } ownerUserId)
        {
            var ownerIsUsable = await db.Users
                .AnyAsync(u => u.Id == ownerUserId && u.IsActive, ct);
            if (ownerIsUsable) return [ownerUserId];
        }

        return await ProcurementOfficersAsync(db, rfq.OrganizationId, ct);
    }

    /// <summary>
    /// A-7: §3.1's "the approver" as a person - the manager the current review pass was assigned to.
    ///
    /// <para>Falls back to the manager pool when the pass named nobody, which is the normal case
    /// while approval routing is undecided (see <c>Rfq.SubmitForReview</c>), and when the named
    /// approver's account is no longer active.</para>
    /// </summary>
    public static async Task<List<Guid>> RfqApproverAsync(AppDbContext db, Rfq rfq, CancellationToken ct)
    {
        var assigned = rfq.Approvals.LastOrDefault(a => a.Decision is null)?.AssignedApproverUserId;
        if (assigned is { } approverUserId)
        {
            var approverIsUsable = await db.Users.AnyAsync(u => u.Id == approverUserId && u.IsActive, ct);
            if (approverIsUsable) return [approverUserId];
        }

        return await ProcurementManagersAsync(db, rfq.OrganizationId, ct);
    }

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
