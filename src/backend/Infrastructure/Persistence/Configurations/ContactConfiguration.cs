// How one of a supplier's contact people maps to its table.
//
// The email column is long enough for the longest address the standard permits.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> entity)
    {
        entity.ToTable("contact", "supplier");
        entity.HasKey(c => c.Id);
        entity.Property(c => c.FullName).HasMaxLength(200).IsRequired();
        entity.Property(c => c.Email).HasMaxLength(320).IsRequired();
        entity.HasIndex(c => c.SupplierId);
    }
}
