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

internal sealed class RfqAttachmentConfiguration : IEntityTypeConfiguration<RfqAttachment>
{
    public void Configure(EntityTypeBuilder<RfqAttachment> entity)
    {
        entity.ToTable("rfq_attachment", "rfq");
        entity.Property(a => a.ScanState).HasConversion<string>().HasMaxLength(20);
        entity.HasKey(a => a.Id);
        entity.Property(a => a.StorageKey).HasMaxLength(500).IsRequired();
        entity.Property(a => a.OriginalFileName).HasMaxLength(300).IsRequired();
        entity.Property(a => a.ContentType).HasMaxLength(150).IsRequired();
        entity.Property(a => a.Caption).HasMaxLength(500);
        entity.HasIndex(a => a.RfqId);
    }
}
