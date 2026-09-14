// How one of a supplier's branches maps to its table.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> entity)
    {
        entity.ToTable("branch", "supplier");
        entity.HasKey(b => b.Id);
        entity.Property(b => b.NameAr).HasMaxLength(200).IsRequired();
        entity.Property(b => b.NameEn).HasMaxLength(200).IsRequired();
        entity.HasIndex(b => b.SupplierId);
    }
}
