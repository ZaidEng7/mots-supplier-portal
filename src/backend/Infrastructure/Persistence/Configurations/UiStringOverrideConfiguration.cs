// How an administrator's rewording of one interface string maps to its table.
//
// The key column is generous for a dotted translation path; the longest in the bundle today is under
// sixty characters.
//
// The value column is long because an override replaces a whole sentence in some places, and one
// read-only explanation in the product is over three hundred characters. Truncating a rewording is worse
// than allowing a long one.
//
// One row per key per language, because rewording an English label is not rewording the Arabic one.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

internal sealed class UiStringOverrideConfiguration : IEntityTypeConfiguration<Domain.Configuration.UiStringOverride>
{
    public void Configure(EntityTypeBuilder<Domain.Configuration.UiStringOverride> entity)
    {
        entity.ToTable("ui_string_override", "ops");
        entity.Property(o => o.RowVersion).IsAppManagedVersion();
        entity.HasKey(o => o.Id);
        entity.Property(o => o.Key).HasMaxLength(200).IsRequired();
        entity.Property(o => o.Language).HasMaxLength(8).IsRequired();
        entity.Property(o => o.Value).HasMaxLength(2000).IsRequired();
        entity.HasIndex(o => new { o.Key, o.Language }).IsUnique();
    }
}
