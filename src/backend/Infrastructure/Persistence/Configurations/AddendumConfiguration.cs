// How a published change to a tender maps to its table.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Rfqs;

internal sealed class AddendumConfiguration : IEntityTypeConfiguration<Addendum>
{
    public void Configure(EntityTypeBuilder<Addendum> entity)
    {
        entity.ToTable("addendum", "rfq");
        entity.HasKey(a => a.Id);
        entity.Property(a => a.TitleAr).HasMaxLength(300).IsRequired();
        entity.Property(a => a.TitleEn).HasMaxLength(300).IsRequired();
        entity.Property(a => a.DescriptionAr).HasMaxLength(4000).IsRequired();
        entity.Property(a => a.DescriptionEn).HasMaxLength(4000).IsRequired();
        entity.HasIndex(a => a.RfqId);
    }
}
