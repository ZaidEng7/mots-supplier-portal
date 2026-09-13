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

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> entity)
    {
        entity.ToTable("currencies", "reference");
        entity.HasKey(c => c.Id);
        entity.Property(c => c.Code).HasMaxLength(3).IsRequired();
        entity.HasIndex(c => c.Code).IsUnique();
        entity.Property(c => c.NameAr).HasMaxLength(100).IsRequired();
        entity.Property(c => c.NameEn).HasMaxLength(100).IsRequired();

        entity.HasData(
            new Currency { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Code = "SYP", NameAr = "ليرة سورية", NameEn = "Syrian Pound" },
            new Currency { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Code = "USD", NameAr = "دولار أمريكي", NameEn = "US Dollar" }
        );
    }
}
