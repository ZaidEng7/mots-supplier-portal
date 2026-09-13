// How one person's choice about one notification type maps to its table.
//
// One row per person and type. A duplicate would mute the same type twice, which changes nothing, and
// that is exactly why it must be refused here rather than tidied up afterwards.
//
// A set of preferences the user sends twice has to be idempotent at the database, not in whichever
// handler happens to write it.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Identity;

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> entity)
    {
        entity.ToTable("notification_preference", "shared");
        entity.HasKey(p => p.Id);
        entity.Property(p => p.NotificationType).HasMaxLength(200).IsRequired();

        entity.HasIndex(p => new { p.UserId, p.NotificationType }).IsUnique();

        entity.HasOne<AppUser>().WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
