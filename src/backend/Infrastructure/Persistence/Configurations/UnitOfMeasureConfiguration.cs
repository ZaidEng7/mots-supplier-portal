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

        // FEAT-06.1 [ASSUMPTION]: minimal interim list matching the hospitality/tourism sector
        // Category.cs already seeds (accommodation, catering, transport, tours, events).
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
