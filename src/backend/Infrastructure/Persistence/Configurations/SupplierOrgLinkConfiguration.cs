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
