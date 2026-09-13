// How an uploaded document maps to its table.
//
// Its public identifier is unique in the database rather than merely in the generator. The generator is
// atomic, but a unique index is what makes a collision impossible rather than unlikely, and it is what
// every other reference code in this schema already has.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

internal sealed class SupplierDocumentConfiguration : IEntityTypeConfiguration<SupplierDocument>
{
    public void Configure(EntityTypeBuilder<SupplierDocument> entity)
    {
        entity.ToTable("supplier_document", "supplier");
        entity.Property(d => d.ReferenceCode).HasMaxLength(30).IsRequired();
        entity.HasIndex(d => d.ReferenceCode).IsUnique();
        entity.HasKey(d => d.Id);
        entity.Property(d => d.State).HasConversion<string>().HasMaxLength(20);
        entity.Property(d => d.StorageKey).HasMaxLength(500).IsRequired();
        entity.Property(d => d.OriginalFileName).HasMaxLength(300).IsRequired();
        entity.Property(d => d.ContentType).HasMaxLength(150).IsRequired();
        entity.Property(d => d.RejectReason).HasMaxLength(1000);
        entity.HasIndex(d => new { d.SupplierId, d.DocumentTypeId, d.IsLatestVersion });
        entity.HasOne<Supplier>().WithMany().HasForeignKey(d => d.SupplierId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Domain.ReferenceData.DocumentType>().WithMany().HasForeignKey(d => d.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
