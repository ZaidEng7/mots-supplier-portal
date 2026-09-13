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

internal sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<Domain.ReferenceData.DocumentType>
{
    public void Configure(EntityTypeBuilder<Domain.ReferenceData.DocumentType> entity)
    {
        entity.ToTable("document_type", "reference");
        entity.HasKey(d => d.Id);
        entity.Property(d => d.Code).HasMaxLength(50).IsRequired();
        entity.HasIndex(d => d.Code).IsUnique();
        entity.Property(d => d.NameAr).HasMaxLength(200).IsRequired();
        entity.Property(d => d.NameEn).HasMaxLength(200).IsRequired();

        // Generic types only - no invented Syrian-specific document rules (FR-REG-006 pattern).
        //
        // IsAwardCritical is D-58's ruling, and it belongs HERE rather than in a data migration. It was
        // a migration first (20260908115449, folded into the squash), and that only worked while the
        // migration history was replayed from the beginning: a squashed baseline seeds this table from
        // the model, so a flag that lived only in an UpdateData step would have come back false and
        // BRULE-023 would have gone back to suspending nobody. The seeded value is the product's
        // answer; SCR-710 is for a buying body that needs a different one.
        entity.HasData(
            new Domain.ReferenceData.DocumentType
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000101"),
                Code = "commercial_registration",
                NameAr = "السجل التجاري",
                NameEn = "Commercial Registration",
                IsRequired = true,
                ExpiryTracked = false,
                // An expired commercial register means the entity is no longer registered to trade.
                IsAwardCritical = true,
            },
            new Domain.ReferenceData.DocumentType
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000102"),
                Code = "tax_certificate",
                NameAr = "الشهادة الضريبية",
                NameEn = "Tax Certificate",
                IsRequired = true,
                ExpiryTracked = true,
                // An expired tax card means the company cannot lawfully be paid.
                IsAwardCritical = true,
            },
            new Domain.ReferenceData.DocumentType
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000103"),
                Code = "chamber_membership",
                NameAr = "عضوية الغرفة التجارية",
                NameEn = "Chamber of Commerce Membership",
                IsRequired = false,
                ExpiryTracked = true,
            }
        );
    }
}
