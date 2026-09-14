// Resolving the recipient groups the written transition tables name.
//
// Recipients come from those tables rather than from inference. "Notify the committee" and "notify the officer"
// are different sets, and the table says which for every transition, so each method here corresponds to a phrase
// in that column rather than to a convenient query.
//
//
// "THE OFFICER" IS NOW A PERSON
//
// It used to resolve to the whole role-and-organization pool, because nothing recorded which officer owned a
// tender. Ownership does now, so the phrase resolves to that person.
//
// It falls back to the pool in two cases, both deliberate: a tender created before ownership existed has no
// owner, and an owner whose account has since been deactivated cannot read a notification.
//
// In either case the alternative to the pool is nobody, and a transition in a live tender that notifies nobody is
// the failure the ownership ruling exists to prevent. So the fallback is wider than ideal on purpose rather than
// silent.
//
// The same shape holds for "the approver": the manager the current review pass was assigned to, falling back to
// the manager pool when the pass named nobody, which is the normal case while approval routing is undecided.
//
//
// AWARD APPROVAL IS STILL A POOL, DELIBERATELY
//
// Which manager an award routes to is an amount-threshold question the ministry has not settled.
//
// Naming one here would invent the routing rule rather than record a decision, so it stays the pool and stays
// reported as such.
//
//
// "COMMITTEE" IS AN INTERPRETATION, NAMED ONCE
//
// No committee entity exists. The tables use the word for the internal group that runs a tender, which in this
// domain is exactly the officers and managers scoped to the organization.
//
// It is named here so the interpretation lives in one place rather than being repeated at each call site.
//
// A platform-level alert is not organization-scoped, because an integration failure is not one buying body's
// problem.

namespace MotsSupplierPortal.Infrastructure.Notifications;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

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

    public static Task<List<Guid>> ProcurementManagersAsync(AppDbContext db, Guid organizationId, CancellationToken ct) =>
        InRoleForOrganization(db, Roles.ProcurementManager, organizationId).Distinct().ToListAsync(ct);

    public static Task<List<Guid>> ProcurementOfficersAsync(AppDbContext db, Guid organizationId, CancellationToken ct) =>
        InRoleForOrganization(db, Roles.ProcurementOfficer, organizationId).Distinct().ToListAsync(ct);

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

    public static async Task<List<Guid>> CommitteeAsync(AppDbContext db, Guid organizationId, CancellationToken ct)
    {
        var officers = await ProcurementOfficersAsync(db, organizationId, ct);
        var managers = await ProcurementManagersAsync(db, organizationId, ct);
        return [.. officers.Concat(managers).Distinct()];
    }

    public static Task<List<Guid>> AwardApproversAsync(AppDbContext db, Guid organizationId, CancellationToken ct) =>
        InRoleForOrganization(db, Roles.ProcurementManager, organizationId).Distinct().ToListAsync(ct);

    public static Task<List<Guid>> SystemAdminsAsync(AppDbContext db, CancellationToken ct) =>
        InRole(db, Roles.SystemAdmin).Distinct().ToListAsync(ct);

    public static Task<List<Guid>> RfqInviteeUsersAsync(AppDbContext db, Guid rfqId, CancellationToken ct) =>
        (from i in db.Invitations
         join u in db.Users on i.SupplierId equals u.SupplierId
         where i.RfqId == rfqId
         select u.Id).Distinct().ToListAsync(ct);

    public static Task<List<Guid>> AssignedEvaluatorsAsync(AppDbContext db, Guid evaluationId, CancellationToken ct) =>
        db.EvaluationAssignments
            .Where(a => a.EvaluationId == evaluationId && a.RecusedAt == null)
            .Select(a => a.EvaluatorUserId)
            .Distinct()
            .ToListAsync(ct);

    public static Task<List<Guid>> SupplierUsersAsync(AppDbContext db, Guid supplierId, CancellationToken ct) =>
        db.Users.Where(u => u.SupplierId == supplierId).Select(u => u.Id).Distinct().ToListAsync(ct);
}
