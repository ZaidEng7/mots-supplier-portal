// How a Syrian governorate maps to its table, and the four the product is seeded with.
//
// The four seeded are the governorates with the ministry's own directorates. The rest are added on the
// reference-data screen rather than invented here.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

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
