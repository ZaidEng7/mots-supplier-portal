// How a currency maps to its table, and the two the product ships with.
//
// The code column is three characters, matching the standard's own codes. A column that accepts fifty
// invites free text back in through the administration screen.
//
// Two currencies are seeded because they are the two the ministry actually transacts in. A buying body
// needing another adds it on the reference-data screen.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.ReferenceData;

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> entity)
    {
        entity.ToTable("currencies", "reference");
        entity.HasKey(c => c.Id);
        entity.Property(c => c.Code).HasMaxLength(3).IsRequired();
        entity.HasIndex(c => c.Code).IsUnique();
        entity.Property(c => c.NameAr).HasMaxLength(100).IsRequired();
        entity.Property(c => c.NameEn).HasMaxLength(100).IsRequired();

        entity.HasData(
            new Currency { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Code = "SYP", NameAr = "ليرة سورية", NameEn = "Syrian Pound" },
            new Currency { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar" }
        );
    }
}
