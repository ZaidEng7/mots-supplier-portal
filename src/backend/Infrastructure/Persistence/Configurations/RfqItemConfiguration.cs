// How one requested line of a tender maps to its table.
//
// One row per tender and line number, so the numbering a bidder quotes back is unambiguous.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Rfqs;

internal sealed class RfqItemConfiguration : IEntityTypeConfiguration<RfqItem>
{
    public void Configure(EntityTypeBuilder<RfqItem> entity)
    {
        entity.ToTable("rfq_item", "rfq");
        entity.HasKey(i => i.Id);
        entity.Property(i => i.TitleAr).HasMaxLength(300).IsRequired();
        entity.Property(i => i.TitleEn).HasMaxLength(300).IsRequired();
        entity.Property(i => i.SpecificationAr).HasMaxLength(2000);
        entity.Property(i => i.SpecificationEn).HasMaxLength(2000);
        entity.Property(i => i.CategoryCode).HasMaxLength(50).IsRequired();
        entity.Property(i => i.Quantity).HasPrecision(18, 4);
        entity.Property(i => i.UnitOfMeasureCode).HasMaxLength(50).IsRequired();
        entity.HasIndex(i => new { i.RfqId, i.LineNo }).IsUnique();
    }
}
