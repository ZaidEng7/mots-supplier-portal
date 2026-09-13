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

internal sealed class SupplierDocumentConfiguration : IEntityTypeConfiguration<SupplierDocument>
{
    public void Configure(EntityTypeBuilder<SupplierDocument> entity)
    {
        entity.ToTable("supplier_document", "supplier");
        // T-010: the public identifier. Unique in the DATABASE, not merely in the generator - the
        // generator is atomic (MSP-81) but a unique index is what makes a collision impossible rather
        // than unlikely, and it is what every other reference code in this schema already has.
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
