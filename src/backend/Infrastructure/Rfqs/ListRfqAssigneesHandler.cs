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

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>
/// A-7: the staff this RFQ can be assigned to, for the two pickers that need them.
///
/// <para><b>Gated on <c>rfq.read</c>, not on a staff-administration permission.</b> The two callers
/// are a procurement officer nominating an approver and a manager reassigning ownership, and neither
/// holds <c>admin.users.manage</c> - gating this on that permission would put a picker on screen that
/// only a system administrator could fill. What it discloses is the names of colleagues in the
/// caller's OWN organization, which BRULE-029 already treats as one boundary, and nothing else: no
/// email, no MFA state, no lockout.</para>
///
/// <para><b>A supplier gets a 404, without a special case.</b> Supplier scopes carry no
/// OrganizationId, so <c>LoadScopedAsync</c> finds nothing - which is §9.2's answer anyway. Asserted
/// in the tests rather than left to be inferred from this paragraph.</para>
/// </summary>
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
