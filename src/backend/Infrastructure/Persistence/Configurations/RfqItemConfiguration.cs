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
