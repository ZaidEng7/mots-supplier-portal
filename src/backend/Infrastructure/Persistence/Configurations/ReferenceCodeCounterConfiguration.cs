// The table that hands out the next public reference code for each prefix.
//
// The prefix is the key, and there is no separate identifier column. A second row for the same prefix
// would be a second allocator competing with the first.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

internal sealed class ReferenceCodeCounterConfiguration : IEntityTypeConfiguration<ReferenceCodeCounter>
{
    public void Configure(EntityTypeBuilder<ReferenceCodeCounter> entity)
    {
        entity.ToTable("reference_code_counter", "supplier");
        entity.HasKey(c => c.Prefix);
        entity.Property(c => c.Prefix).HasMaxLength(30);
        entity.Property(c => c.LastValue).IsRequired();
    }
}
