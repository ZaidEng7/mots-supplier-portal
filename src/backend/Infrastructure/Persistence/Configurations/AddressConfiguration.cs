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

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> entity)
    {
        entity.ToTable("address", "supplier");
        entity.HasKey(a => a.Id);
        entity.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        entity.Property(a => a.Line1).HasMaxLength(300).IsRequired();
        entity.Property(a => a.Line2).HasMaxLength(300);
        entity.Property(a => a.City).HasMaxLength(100).IsRequired();
        entity.Property(a => a.RegionCode).HasMaxLength(20).IsRequired();
        entity.Property(a => a.Country).HasMaxLength(100).IsRequired();
        entity.Property(a => a.PostalCode).HasMaxLength(20);
        entity.HasIndex(a => a.SupplierId);
    }
}
