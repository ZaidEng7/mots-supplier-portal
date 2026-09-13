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

internal sealed class DocumentTypeCategoryConfiguration : IEntityTypeConfiguration<Domain.ReferenceData.DocumentTypeCategory>
{
    public void Configure(EntityTypeBuilder<Domain.ReferenceData.DocumentTypeCategory> entity)
    {
        entity.ToTable("document_type_category", "reference");
        entity.HasKey(l => l.Id);
        entity.Property(l => l.CategoryCode).HasMaxLength(50).IsRequired();
        // One link per (type, category). A duplicate would double-count nothing today and would
        // double-count a requirement the day the derivation is switched on.
        entity.HasIndex(l => new { l.DocumentTypeId, l.CategoryCode }).IsUnique();
        // Cascade from the document type, because a link to a type that no longer exists is not a
        // historical record of anything - unlike the reference CODES themselves, which D-28 keeps.
        entity.HasOne<Domain.ReferenceData.DocumentType>()
            .WithMany()
            .HasForeignKey(l => l.DocumentTypeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
