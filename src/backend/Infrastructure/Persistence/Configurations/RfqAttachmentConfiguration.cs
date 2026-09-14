// How a file attached to a tender maps to its table.
//
// The scan state is the virus scanner's verdict, and a file is not served to bidders until it says so.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Rfqs;

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
