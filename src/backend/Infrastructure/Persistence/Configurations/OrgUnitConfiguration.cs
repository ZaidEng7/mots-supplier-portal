using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

internal sealed class OrgUnitConfiguration : IEntityTypeConfiguration<OrgUnit>
{
    public void Configure(EntityTypeBuilder<OrgUnit> entity)
    {
        entity.ToTable("org_unit", "organization");
        entity.HasKey(u => u.Id);
        entity.Property(u => u.Name).HasMaxLength(200).IsRequired();
        entity.HasIndex(u => u.OrganizationId);
        // Self-nesting tree (§5.2): a unit's parent must be another unit in the same
        // Organization, never a unit belonging elsewhere - restricted to that same FK target
        // rather than a bare unconstrained Guid, and Restrict (not Cascade) so deleting a
        // parent unit cannot silently cascade-delete its children.
        entity.HasOne<OrgUnit>().WithMany().HasForeignKey(u => u.ParentOrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
