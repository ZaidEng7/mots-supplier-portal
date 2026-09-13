using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Organizations;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

namespace MotsSupplierPortal.Infrastructure.Organizations;

public sealed class ManageOrgUnitHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageOrgUnitHandler
{
    // AddAsync and RemoveAsync repeated this exact lookup line-for-line - a real, literal
    // duplicate (not eyeballed-similar), extracted as a likely contributor to the new-code
    // duplication ratchet failure. No line-level location was available from Sonar for this PR
    // (dashboard access requires a login this session doesn't have); this is the one exact match
    // found by direct inspection, not a confirmed match against Sonar's own report.
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

        // OrgUnit.Id is client-assigned (Guid.CreateVersion7()), so EF's graph-tracking heuristic
        // would otherwise mark it Modified (a no-op UPDATE against a row that doesn't exist yet -
        // 0 rows affected, DbUpdateConcurrencyException) instead of Added - the exact CategoryLink
        // trap (ManageCategoryLinkHandler's own comment) - track it explicitly.
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

        // A unit with children cannot be removed here: the FK from a child's ParentOrgUnitId is
        // Restrict (AppDbContext.cs), by design (Stage B's own reasoning: a parent's removal must
        // never silently cascade-delete its children) - surfacing that as a clear domain-level
        // refusal is better than letting the caller hit a raw Postgres FK-violation instead.
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
