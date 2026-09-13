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
