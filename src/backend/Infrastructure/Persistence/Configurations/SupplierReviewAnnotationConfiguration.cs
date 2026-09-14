// How a reviewer's note on a registration maps to its table.
//
// The two flagged lists are stored as arrays rather than as separate rows, because they are read whole
// with the note and never queried across notes.
//
// The index pairs the supplier with the resolution, which is the question the review screen asks: what on
// this supplier is still outstanding.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

internal sealed class SupplierReviewAnnotationConfiguration : IEntityTypeConfiguration<SupplierReviewAnnotation>
{
    public void Configure(EntityTypeBuilder<SupplierReviewAnnotation> entity)
    {
        entity.ToTable("supplier_review_annotation", "supplier");
        entity.HasKey(a => a.Id);
        entity.Property(a => a.Reason).HasMaxLength(2000).IsRequired();
        entity.Property(a => a.FlaggedProfileFields).HasColumnType("text[]");
        entity.Property(a => a.FlaggedDocumentTypeIds).HasColumnType("uuid[]");
        entity.HasIndex(a => new { a.SupplierId, a.ResolvedAt });
    }
}
