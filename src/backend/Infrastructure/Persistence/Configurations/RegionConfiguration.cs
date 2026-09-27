// How a Syrian governorate maps to its table, and the fourteen the product is seeded with.
//
// ALL FOURTEEN ARE SEEDED, AND THAT IS A CHANGE. Four were seeded before - the governorates with the ministry's
// own directorates - on the reasoning that the rest belonged on the reference-data screen rather than being
// invented here. The reasoning was sound about inventing things and wrong about these: Syria's governorates are
// an official administrative division, not a guess, and the four-entry list was about to cost real data.
//
// WHAT FORCED IT. Supplier records are to be imported from the ministry's ERP, whose address carries a
// free-text province. A supplier in Tartus or Deir ez-Zor had nowhere to be filed, and because there is no
// foreign key on Address.RegionCode and no validation behind the address routes, the import would not have
// failed - it would have written a code with no row behind it, exactly the way 29 demo suppliers once carried
// the governorate "DM". See SeededReferenceCodeTests for that incident.
//
// THE CODES ARE THREE LETTERS AND THE EXISTING FOUR DO NOT CHANGE. DIM, ALP, LAT and HOM are already written
// into supplier addresses, so they are fixed whatever one thinks of a list that transliterates Dimashq and
// anglicises Aleppo in the same breath. The ten added follow the same shape.
//
// THE ORDER CARRIES NO MEANING. The original four keep their identifiers, and the rest follow alphabetically by
// English name. Anything that wants a display order should sort on the name it is showing.

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
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000204"), Code = "HOM", NameAr = "حمص", NameEn = "Homs" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000205"), Code = "DAR", NameAr = "درعا", NameEn = "Daraa" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000206"), Code = "DEZ", NameAr = "دير الزور", NameEn = "Deir ez-Zor" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000207"), Code = "HAS", NameAr = "الحسكة", NameEn = "Al-Hasakah" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000208"), Code = "HMA", NameAr = "حماة", NameEn = "Hama" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000209"), Code = "IDL", NameAr = "إدلب", NameEn = "Idlib" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000210"), Code = "QUN", NameAr = "القنيطرة", NameEn = "Quneitra" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000211"), Code = "RAQ", NameAr = "الرقة", NameEn = "Raqqa" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000212"), Code = "RDM", NameAr = "ريف دمشق", NameEn = "Rif Dimashq" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000213"), Code = "SUW", NameAr = "السويداء", NameEn = "As-Suwayda" },
            new Domain.ReferenceData.Region { Id = Guid.Parse("00000000-0000-0000-0000-000000000214"), Code = "TAR", NameAr = "طرطوس", NameEn = "Tartus" }
        );
    }
}
