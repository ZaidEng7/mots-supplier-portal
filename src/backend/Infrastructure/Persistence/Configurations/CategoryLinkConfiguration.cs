// How a supplier's claim to a category maps to its table.
//
// One claim per supplier and category. A duplicate would make the same supplier appear twice under one
// category in the buyer's directory.
//
// The filtered unique index is what keeps "exactly one primary" true in the database rather than only in
// the aggregate. The domain already enforces it on every path, but a backfill, a repair script or a future
// bulk update reaches the table directly, and this is the only place that would stop two primaries landing
// there. It is filtered on IsPrimary so the many non-primary rows per supplier remain legal.

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
        entity.HasIndex(l => l.SupplierId)
            .IsUnique()
            .HasFilter("\"IsPrimary\"")
            .HasDatabaseName("ix_category_link_one_primary_per_supplier");
    }
}
