// How a buying body maps to its table.
//
// Its public code, in the shape ORG-2026-000001, is unique, like every other reference code in this
// schema.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Organizations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> entity)
    {
        entity.ToTable("organization", "organization");
        entity.HasKey(o => o.Id);
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
