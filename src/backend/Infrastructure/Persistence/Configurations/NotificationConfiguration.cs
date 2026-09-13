// How a stored notification maps to its table.
//
// Its version column maps to the database's own row version, the same way every other versioned record
// in this schema does.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Identity;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> entity)
    {
        entity.ToTable("notification", "shared");
        entity.HasKey(n => n.Id);
        entity.Property(n => n.Type).HasMaxLength(200).IsRequired();
        entity.Property(n => n.Channel).HasConversion<string>().HasMaxLength(20);
        entity.Property(n => n.DeliveryStatus).HasConversion<string>().HasMaxLength(20);
        entity.Property(n => n.TitleAr).HasMaxLength(300).IsRequired();
        entity.Property(n => n.TitleEn).HasMaxLength(300).IsRequired();
        entity.Property(n => n.BodyAr).HasMaxLength(2000).IsRequired();
        entity.Property(n => n.BodyEn).HasMaxLength(2000).IsRequired();
        entity.Property(n => n.DataJson).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        entity.Property(n => n.DedupeKey).HasMaxLength(400).IsRequired();

        entity.HasIndex(n => n.DedupeKey).IsUnique();
        entity.HasIndex(n => new { n.RecipientUserId, n.ReadAt });

        entity.HasOne<AppUser>().WithMany()
            .HasForeignKey(n => n.RecipientUserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.Property(n => n.RowVersion).IsAppManagedVersion();
    }
}
