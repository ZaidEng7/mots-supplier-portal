// How the link between a supplier and a buying body maps to its table.
//
// One link per pair. Deleting either side deletes the link, because a link to something that no longer
// exists is not a historical record of anything.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Suppliers;

internal sealed class SupplierOrgLinkConfiguration : IEntityTypeConfiguration<SupplierOrgLink>
{
    public void Configure(EntityTypeBuilder<SupplierOrgLink> entity)
    {
        entity.ToTable("supplier_org_link", "organization");
        entity.HasKey(l => l.Id);
        entity.HasIndex(l => new { l.SupplierId, l.OrganizationId }).IsUnique();
        entity.HasOne<Supplier>().WithMany().HasForeignKey(l => l.SupplierId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Organization>().WithMany().HasForeignKey(l => l.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}
