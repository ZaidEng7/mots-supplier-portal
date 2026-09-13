// How a supplier's claim to a category maps to its table.
//
// One claim per supplier and category. A duplicate would make the same supplier appear twice under one
// category in the buyer's directory.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

internal sealed class CategoryLinkConfiguration : IEntityTypeConfiguration<CategoryLink>
{
    public void Configure(EntityTypeBuilder<CategoryLink> entity)
    {
        entity.ToTable("category_link", "supplier");
        entity.HasKey(l => l.Id);
        entity.Property(l => l.CategoryCode).HasMaxLength(50).IsRequired();
        entity.HasIndex(l => new { l.SupplierId, l.CategoryCode }).IsUnique();
    }
}
