// How a document type maps to its table, and the types the product ships with.
//
// The seeded types are generic. No Syrian-specific document rules are invented here, following the same
// approach the registration fields take.
//
//
// WHY THE AWARD-CRITICAL FLAG IS SEEDED HERE AND NOT IN A DATA MIGRATION
//
// It was a migration first, and that only worked while the migration history was replayed from the
// beginning.
//
// A squashed baseline seeds this table from the model, so a flag that lived only in an update step would
// have come back false, and the rule that suspends a supplier when an award-critical document expires
// would have gone back to suspending nobody.
//
// The seeded value is the product's answer. A buying body that needs a different one changes it on the
// reference-data screen.
//
// Two types are award-critical as shipped. An expired commercial register means the entity is no longer
// registered to trade. An expired tax card means the company cannot lawfully be paid.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

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

        entity.HasData(
            new Domain.ReferenceData.DocumentType
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000101"),
                Code = "commercial_registration",
                NameAr = "السجل التجاري",
                NameEn = "Commercial Registration",
                IsRequired = true,
                ExpiryTracked = false,
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
