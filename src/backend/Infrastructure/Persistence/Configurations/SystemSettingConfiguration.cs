// How an administrator's setting maps to its table.
//
// It is deliberately not seeded. An absent row means nobody has decided, and every reader falls back to
// configuration and then to the setting's own default, so an environment that never opens the settings
// screen behaves exactly as it did before this table existed.
//
// Seeding the defaults would erase that distinction and turn "unset" into "an administrator chose
// thirty", which is the fact the audit trail is supposed to carry.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<Domain.Configuration.SystemSetting>
{
    public void Configure(EntityTypeBuilder<Domain.Configuration.SystemSetting> entity)
    {
        entity.ToTable("system_setting", "ops");
        entity.Property(s => s.RowVersion).IsAppManagedVersion();
        entity.HasKey(s => s.Id);
        entity.Property(s => s.Key).HasMaxLength(100).IsRequired();
        entity.Property(s => s.Value).HasMaxLength(500).IsRequired();
        entity.HasIndex(s => s.Key).IsUnique();

    }
}
