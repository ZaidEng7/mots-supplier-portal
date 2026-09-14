// How a unit of measure maps to its table, and the interim list it is seeded with.
//
// The seeded list is a minimal one matching the sectors the category table already seeds: accommodation,
// catering, transport, tours and events. It is an assumption recorded as such rather than a researched
// list.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

internal sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<Domain.ReferenceData.UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<Domain.ReferenceData.UnitOfMeasure> entity)
    {
        entity.ToTable("unit_of_measure", "reference");
        entity.HasKey(u => u.Id);
        entity.Property(u => u.Code).HasMaxLength(50).IsRequired();
        entity.HasIndex(u => u.Code).IsUnique();
        entity.Property(u => u.NameAr).HasMaxLength(150).IsRequired();
        entity.Property(u => u.NameEn).HasMaxLength(150).IsRequired();

        entity.HasData(
            new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000501"), Code = "night", NameAr = "ليلة", NameEn = "Night" },
            new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000502"), Code = "person", NameAr = "شخص", NameEn = "Person" },
            new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000503"), Code = "trip", NameAr = "رحلة", NameEn = "Trip" },
            new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000504"), Code = "hour", NameAr = "ساعة", NameEn = "Hour" },
            new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000505"), Code = "day", NameAr = "يوم", NameEn = "Day" },
            new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000506"), Code = "unit", NameAr = "وحدة", NameEn = "Unit" },
            new Domain.ReferenceData.UnitOfMeasure { Id = Guid.Parse("00000000-0000-0000-0000-000000000507"), Code = "event", NameAr = "فعالية", NameEn = "Event" }
        );
    }
}
