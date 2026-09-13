// How a department maps to its table.
//
// Departments nest, and a department's parent must be another department in the same organization rather
// than one belonging elsewhere. That is why the parent points at the same table rather than being an
// unconstrained identifier.
//
// Deleting a parent department is restricted rather than cascading, so it cannot silently delete its
// children.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Organizations;

internal sealed class OrgUnitConfiguration : IEntityTypeConfiguration<OrgUnit>
{
    public void Configure(EntityTypeBuilder<OrgUnit> entity)
    {
        entity.ToTable("org_unit", "organization");
        entity.HasKey(u => u.Id);
        entity.Property(u => u.Name).HasMaxLength(200).IsRequired();
        entity.HasIndex(u => u.OrganizationId);
        entity.HasOne<OrgUnit>().WithMany().HasForeignKey(u => u.ParentOrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
