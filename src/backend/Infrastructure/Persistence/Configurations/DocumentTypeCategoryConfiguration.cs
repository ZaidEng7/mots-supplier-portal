// How the link between a document type and a category maps to its table.
//
// One link per pair. A duplicate would double-count nothing today, and would double-count a requirement
// the day the narrowing it feeds is switched on.
//
// Deleting a document type deletes its links, because a link to a type that no longer exists is not a
// historical record of anything. That is unlike the reference codes themselves, which are never deleted.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

internal sealed class DocumentTypeCategoryConfiguration : IEntityTypeConfiguration<Domain.ReferenceData.DocumentTypeCategory>
{
    public void Configure(EntityTypeBuilder<Domain.ReferenceData.DocumentTypeCategory> entity)
    {
        entity.ToTable("document_type_category", "reference");
        entity.HasKey(l => l.Id);
        entity.Property(l => l.CategoryCode).HasMaxLength(50).IsRequired();
        entity.HasIndex(l => new { l.DocumentTypeId, l.CategoryCode }).IsUnique();
        entity.HasOne<Domain.ReferenceData.DocumentType>()
            .WithMany()
            .HasForeignKey(l => l.DocumentTypeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
