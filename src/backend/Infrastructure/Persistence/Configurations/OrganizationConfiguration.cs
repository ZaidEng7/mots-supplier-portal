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

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> entity)
    {
        entity.ToTable("organization", "organization");
        entity.HasKey(o => o.Id);
        // T-055: the buying body's public code, ORG-2026-000001. Unique, like every other
        // reference code in this schema.
        entity.Property(o => o.ReferenceCode).HasMaxLength(30).IsRequired();
        entity.HasIndex(o => o.ReferenceCode).IsUnique();
        entity.Property(o => o.LegalNameAr).HasMaxLength(200).IsRequired();
        entity.Property(o => o.LegalNameEn).HasMaxLength(200).IsRequired();
        entity.Property(o => o.OrganizationType).HasConversion<string>().HasMaxLength(20);
        entity.Property(o => o.ContactEmail).HasMaxLength(320);
        entity.Property(o => o.ContactPhone).HasMaxLength(30);
        entity.Property(o => o.ExternalId).HasMaxLength(100);
        entity.Property(o => o.SyncStatus).HasConversion<string>().HasMaxLength(20);
        entity.HasMany(o => o.OrgUnits).WithOne().HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}
