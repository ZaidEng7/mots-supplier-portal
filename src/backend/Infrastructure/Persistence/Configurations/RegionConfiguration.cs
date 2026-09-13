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

internal sealed class RegionConfiguration : IEntityTypeConfiguration<Domain.ReferenceData.Region>
{
    public void Configure(EntityTypeBuilder<Domain.ReferenceData.Region> entity)
    {
        entity.ToTable("region", "reference");
        entity.HasKey(r => r.Id);
        entity.Property(r => r.Code).HasMaxLength(20).IsRequired();
        entity.HasIndex(r => r.Code).IsUnique();
        entity.Property(r => r.NameAr).HasMaxLength(100).IsRequired();
        entity.Property(r => r.NameEn).HasMaxLength(100).IsRequired();

        entity.HasData(
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000201"), Code = "DIM", NameAr = "دمشق", NameEn = "Damascus" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000202"), Code = "ALP", NameAr = "حلب", NameEn = "Aleppo" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000203"), Code = "LAT", NameAr = "اللاذقية", NameEn = "Latakia" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000204"), Code = "HOM", NameAr = "حمص", NameEn = "Homs" }
        );
    }
}
