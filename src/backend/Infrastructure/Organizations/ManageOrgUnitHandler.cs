// Adding and removing a buying body's departments.
//
// The lookup is shared between the two operations. It was repeated line for line in both, which was a literal
// duplicate rather than an eyeballed similarity, extracted when the duplication ratchet failed on new code.
//
// A new department is added to the tracked set explicitly. Its identifier is assigned by us rather than by the
// database, so the graph-tracking heuristic would otherwise take it for an existing row and issue an update
// against a row that is not there yet, which surfaces as a concurrency failure. The same trap the category links
// hit.
//
// A department with children cannot be removed here. The link from a child to its parent is restricted by design,
// because a parent's removal must never silently cascade-delete its children, and surfacing that as a clear
// domain refusal is better than letting the caller hit a raw foreign-key violation.

namespace MotsSupplierPortal.Infrastructure.Organizations;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Organizations;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

public sealed class ManageOrgUnitHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageOrgUnitHandler
{
    private Task<Organization?> LoadOrganizationWithUnitsAsync(Guid organizationId, CancellationToken ct) =>
        db.Set<Organization>().Include(o => o.OrgUnits).FirstOrDefaultAsync(o => o.Id == organizationId, ct);

    public async Task<OrganizationMutationResult> AddAsync(AddOrgUnitCommand command, CancellationToken ct)
    {
        var org = await LoadOrganizationWithUnitsAsync(command.OrganizationId, ct);
        if (org is null) return new OrganizationMutationResult.NotFound();

        OrgUnit unit;
        try
        {
            unit = org.AddOrgUnit(command.Name, command.ParentOrgUnitId);
        }
        catch (DomainException ex)
        {
            return new OrganizationMutationResult.InvalidState(ex.Message);
        }

        db.Set<OrgUnit>().Add(unit);

        await auditLogger.LogAsync("Organization", org.Id, "org_unit_added", scope.UserId, reason: command.Name, ct: ct);
        await db.SaveChangesAsync(ct);
        return new OrganizationMutationResult.Success(OrganizationDtoMapper.ToDto(org));
    }

    public async Task<OrganizationMutationResult> RemoveAsync(RemoveOrgUnitCommand command, CancellationToken ct)
    {
        var org = await LoadOrganizationWithUnitsAsync(command.OrganizationId, ct);
        if (org is null) return new OrganizationMutationResult.NotFound();

        var unit = org.OrgUnits.FirstOrDefault(u => u.Id == command.OrgUnitId);
        if (unit is null) return new OrganizationMutationResult.NotFound();

        if (org.OrgUnits.Any(u => u.ParentOrgUnitId == unit.Id))
        {
            return new OrganizationMutationResult.InvalidState("Cannot remove an OrgUnit that has child units - remove the children first.");
        }

        db.Set<OrgUnit>().Remove(unit);
        await auditLogger.LogAsync("Organization", org.Id, "org_unit_removed", scope.UserId, reason: unit.Name, ct: ct);
        await db.SaveChangesAsync(ct);

        return new OrganizationMutationResult.Success(OrganizationDtoMapper.ToDto(org));
    }
}
