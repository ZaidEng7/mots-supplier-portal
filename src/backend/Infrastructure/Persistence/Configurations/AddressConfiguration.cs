// How one of a supplier's addresses maps to its table.
//
// The kind of address is stored as its name rather than as a position in a list, so a row stays readable
// and reordering the list cannot re-interpret existing rows. Every enumeration in this schema is stored
// the same way.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

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
