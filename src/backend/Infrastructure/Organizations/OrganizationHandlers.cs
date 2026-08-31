using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Organizations;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Organizations;

/// <summary>Task #7/Stage C: admin-only surface. Organization has no public ReferenceCode
/// (Stage A did not add one - DOMAIN-MODEL.md §5.2 lists Address[] as an Organization value
/// object too, also not added in Stage A). Both are real gaps against the design doc, flagged
/// in this stage's report rather than silently expanded into here - this stage builds against
/// what Stage A actually shipped. Internal Guid ids are used directly in these admin routes as a
/// deliberate, scoped exception to the "never expose internal PKs" convention (foundational §2):
/// this surface is staff-only (admin.organizations.manage), never supplier- or public-facing.</summary>
internal static class OrganizationDtoMapper
{
    public static OrganizationDto ToDto(Organization org) => new(
        org.Id, org.LegalNameAr, org.LegalNameEn, org.OrganizationType, org.ContactEmail, org.ContactPhone, org.IsActive,
        [.. org.OrgUnits.Select(u => new OrgUnitDto(u.Id, u.OrganizationId, u.ParentOrgUnitId, u.Name))]);
}

public sealed class CreateOrganizationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ICreateOrganizationHandler
{
    public async Task<OrganizationMutationResult> HandleAsync(CreateOrganizationCommand command, CancellationToken ct)
    {
        Organization org;
        try
        {
            org = Organization.Create(command.LegalNameAr, command.LegalNameEn, command.OrganizationType, command.ContactEmail, command.ContactPhone);
        }
        catch (DomainException ex)
        {
            return new OrganizationMutationResult.InvalidState(ex.Message);
        }

        db.Set<Organization>().Add(org);
        await auditLogger.LogAsync("Organization", org.Id, "organization_created", scope.UserId, reason: command.LegalNameEn, ct: ct);
        await db.SaveChangesAsync(ct);
        return new OrganizationMutationResult.Success(OrganizationDtoMapper.ToDto(org));
    }
}

public sealed class ListOrganizationsHandler(AppDbContext db) : IListOrganizationsHandler
{
    public async Task<IReadOnlyList<OrganizationDto>> HandleAsync(CancellationToken ct)
    {
        var orgs = await db.Set<Organization>().Include(o => o.OrgUnits).OrderBy(o => o.LegalNameEn).ToListAsync(ct);
        return [.. orgs.Select(OrganizationDtoMapper.ToDto)];
    }
}

public sealed class ManageOrgUnitHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageOrgUnitHandler
{
    public async Task<OrganizationMutationResult> AddAsync(AddOrgUnitCommand command, CancellationToken ct)
    {
        var org = await db.Set<Organization>().Include(o => o.OrgUnits).FirstOrDefaultAsync(o => o.Id == command.OrganizationId, ct);
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
        var org = await db.Set<Organization>().Include(o => o.OrgUnits).FirstOrDefaultAsync(o => o.Id == command.OrganizationId, ct);
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

public sealed class ManageSupplierOrgLinkHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageSupplierOrgLinkHandler
{
    public async Task<SupplierOrgLinkMutationResult> CreateAsync(CreateSupplierOrgLinkCommand command, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.ReferenceCode == command.SupplierReferenceCode, ct);
        if (supplier is null) return new SupplierOrgLinkMutationResult.NotFound();

        var orgExists = await db.Set<Organization>().AnyAsync(o => o.Id == command.OrganizationId, ct);
        if (!orgExists) return new SupplierOrgLinkMutationResult.NotFound();

        var alreadyLinked = await db.Set<SupplierOrgLink>().AnyAsync(l => l.SupplierId == supplier.Id && l.OrganizationId == command.OrganizationId, ct);
        if (alreadyLinked) return new SupplierOrgLinkMutationResult.AlreadyLinked();

        var link = SupplierOrgLink.Create(supplier.Id, command.OrganizationId);
        db.Set<SupplierOrgLink>().Add(link);

        await auditLogger.LogAsync("Supplier", supplier.Id, "organization_link_created", scope.UserId, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new SupplierOrgLinkMutationResult.Success(new SupplierOrgLinkDto(link.Id, link.SupplierId, supplier.ReferenceCode, link.OrganizationId, link.CreatedAt));
    }

    public async Task<bool> RemoveAsync(RemoveSupplierOrgLinkCommand command, CancellationToken ct)
    {
        var link = await db.Set<SupplierOrgLink>().FirstOrDefaultAsync(l => l.Id == command.LinkId, ct);
        if (link is null) return false;

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == link.SupplierId, ct);
        db.Set<SupplierOrgLink>().Remove(link);
        await auditLogger.LogAsync("Supplier", link.SupplierId, "organization_link_removed", scope.UserId, referenceCode: supplier?.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<SupplierOrgLinkDto>> ListForSupplierAsync(string supplierReferenceCode, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.ReferenceCode == supplierReferenceCode, ct);
        if (supplier is null) return [];

        return await db.Set<SupplierOrgLink>()
            .Where(l => l.SupplierId == supplier.Id)
            .OrderBy(l => l.CreatedAt)
            .Select(l => new SupplierOrgLinkDto(l.Id, l.SupplierId, supplier.ReferenceCode, l.OrganizationId, l.CreatedAt))
            .ToListAsync(ct);
    }
}
