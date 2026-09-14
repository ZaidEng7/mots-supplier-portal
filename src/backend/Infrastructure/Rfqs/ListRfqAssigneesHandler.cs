// The staff this tender can be assigned to, for the two pickers that need them.
//
//
// GATED ON READING THE TENDER, NOT ON ADMINISTERING USERS
//
// The two callers are an officer nominating an approver and a manager reassigning ownership, and neither
// holds the user-administration permission. Gating this on that permission would put a picker on screen that
// only a system administrator could fill.
//
// What it discloses is the names of colleagues in the caller's OWN organization, which the written rules
// already treat as one boundary, and nothing else: no email address, no second-factor state, no lockout.
//
//
// A SUPPLIER GETS A NOT-FOUND WITHOUT A SPECIAL CASE
//
// A supplier's scope carries no organization, so the scoped load finds nothing, which is the contract's
// answer anyway. Asserted in the tests rather than left to be inferred from this paragraph.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

public sealed class ListRfqAssigneesHandler(AppDbContext db, IScopeContext scope) : IListRfqAssigneesHandler
{
    public async Task<RfqAssigneesDto?> HandleAsync(string referenceCode, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return null;

        var rfq = await db.Rfqs.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReferenceCode == referenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return null;

        var owners = await StaffEligibility.HoldersAsync(db, rfq.OrganizationId, Permissions.RfqEdit, ct);
        var approvers = await StaffEligibility.HoldersAsync(db, rfq.OrganizationId, Permissions.RfqApprove, ct);

        return new RfqAssigneesDto(
            [.. owners.Select(u => new RfqAssigneeDto(u.Id, u.FullName))],
            [.. approvers.Select(u => new RfqAssigneeDto(u.Id, u.FullName))]);
    }
}
