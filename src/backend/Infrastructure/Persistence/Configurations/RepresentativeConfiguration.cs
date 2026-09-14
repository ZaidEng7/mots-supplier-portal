// How an authorised representative of a supplier maps to its table.
//
// The index is unique among primary representatives only, which is how the rule "exactly one primary" is
// enforced by the database rather than by whichever handler happens to set the flag.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

internal sealed class RepresentativeConfiguration : IEntityTypeConfiguration<Representative>
{
    public void Configure(EntityTypeBuilder<Representative> entity)
    {
        entity.ToTable("representative", "supplier");
        entity.HasKey(r => r.Id);
        entity.Property(r => r.FullName).HasMaxLength(200).IsRequired();
        entity.Property(r => r.Email).HasMaxLength(320).IsRequired();
        entity.HasIndex(r => r.SupplierId).HasFilter("\"IsPrimary\" = true").IsUnique();
    }
}
