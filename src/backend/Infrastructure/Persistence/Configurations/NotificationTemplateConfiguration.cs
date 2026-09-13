// How an administrator's notification rewording maps to its table.
//
// It is deliberately not seeded, for the same reason the settings table is not. An absent row means the
// shipped wording is in force, so no deployment's words change until somebody changes them.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

internal sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<Domain.Notifications.NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<Domain.Notifications.NotificationTemplate> entity)
    {
        entity.ToTable("notification_template", "ops");
        entity.Property(t => t.RowVersion).IsAppManagedVersion();
        entity.HasKey(t => t.Id);
        entity.Property(t => t.Type).HasMaxLength(100).IsRequired();
        entity.Property(t => t.TitleAr).HasMaxLength(300).IsRequired();
        entity.Property(t => t.TitleEn).HasMaxLength(300).IsRequired();
        entity.Property(t => t.BodyAr).HasMaxLength(1000).IsRequired();
        entity.Property(t => t.BodyEn).HasMaxLength(1000).IsRequired();
        entity.HasIndex(t => t.Type).IsUnique();

    }
}
