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

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Domain.ReferenceData.Category>
{
    public void Configure(EntityTypeBuilder<Domain.ReferenceData.Category> entity)
    {
        entity.ToTable("category", "reference");
        entity.HasKey(c => c.Id);
        entity.Property(c => c.Code).HasMaxLength(50).IsRequired();
        entity.HasIndex(c => c.Code).IsUnique();
        entity.Property(c => c.NameAr).HasMaxLength(150).IsRequired();
        entity.Property(c => c.NameEn).HasMaxLength(150).IsRequired();

        // MSP-54 [ASSUMPTION]: minimal flat interim list, seeded from Discovery's known
        // MOTS supplier categories - superseded by EPIC-21's real buyer Category tree later.
        entity.HasData(
            new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000301"), Code = "accommodation", NameAr = "الإقامة والفنادق", NameEn = "Accommodation & Hotels" },
            new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000302"), Code = "catering", NameAr = "التموين والضيافة", NameEn = "Catering & Hospitality" },
            new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000303"), Code = "transport", NameAr = "النقل والمواصلات", NameEn = "Transport" },
            new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000304"), Code = "tour_operations", NameAr = "تنظيم الرحلات السياحية", NameEn = "Tour Operations" },
            new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000305"), Code = "events", NameAr = "تنظيم الفعاليات", NameEn = "Events & Conferences" },
            new Domain.ReferenceData.Category { Id = Guid.Parse("00000000-0000-0000-0000-000000000306"), Code = "maintenance", NameAr = "الصيانة والخدمات الفنية", NameEn = "Maintenance & Technical Services" }
        );
    }
}
