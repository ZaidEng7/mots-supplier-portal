using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Organizations;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

namespace MotsSupplierPortal.Infrastructure.Organizations;

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
