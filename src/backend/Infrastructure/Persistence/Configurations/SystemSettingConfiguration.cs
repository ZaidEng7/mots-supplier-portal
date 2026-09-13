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

        // NOT seeded, deliberately. An absent row means "nobody has decided", and every consumer
        // falls back to configuration and then to the definition's default - so an environment
        // that never opens the settings screen behaves exactly as it did before this table
        // existed. Seeding the defaults would erase that distinction and turn "unset" into "an
        // administrator chose 30", which is the fact the audit trail is supposed to carry.
    }
}
