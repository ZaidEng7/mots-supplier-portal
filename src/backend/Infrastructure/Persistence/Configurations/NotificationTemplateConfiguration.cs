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

        // NOT seeded, for the same reason system_setting is not: an absent row means the shipped
        // catalogue is in force, and no deployment's wording changes until somebody changes it.
    }
}
