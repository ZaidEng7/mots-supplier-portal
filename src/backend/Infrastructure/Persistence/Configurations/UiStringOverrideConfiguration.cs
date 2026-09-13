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

internal sealed class UiStringOverrideConfiguration : IEntityTypeConfiguration<Domain.Configuration.UiStringOverride>
{
    public void Configure(EntityTypeBuilder<Domain.Configuration.UiStringOverride> entity)
    {
        entity.ToTable("ui_string_override", "ops");
        entity.Property(o => o.RowVersion).IsAppManagedVersion();
        entity.HasKey(o => o.Id);
        // 200 is generous for a dotted i18n path; the longest in the bundle today is under 60.
        entity.Property(o => o.Key).HasMaxLength(200).IsRequired();
        entity.Property(o => o.Language).HasMaxLength(8).IsRequired();
        // 2000, because an override replaces a whole sentence in some places - SCR-726's read-only
        // explanation is over 300 characters - and truncating a rewording is worse than allowing a
        // long one.
        entity.Property(o => o.Value).HasMaxLength(2000).IsRequired();
        // Per key PER LANGUAGE: rewording an English label is not rewording the Arabic one.
        entity.HasIndex(o => new { o.Key, o.Language }).IsUnique();
    }
}
