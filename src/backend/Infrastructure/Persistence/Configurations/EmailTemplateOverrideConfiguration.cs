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

internal sealed class EmailTemplateOverrideConfiguration : IEntityTypeConfiguration<Domain.Configuration.EmailTemplateOverride>
{
    public void Configure(EntityTypeBuilder<Domain.Configuration.EmailTemplateOverride> entity)
    {
        entity.ToTable("email_template_override", "ops");
        entity.Property(o => o.RowVersion).IsAppManagedVersion();
        entity.HasKey(o => o.Id);
        entity.Property(o => o.Key).HasMaxLength(100).IsRequired();
        entity.Property(o => o.SubjectAr).HasMaxLength(300).IsRequired();
        entity.Property(o => o.SubjectEn).HasMaxLength(300).IsRequired();
        // 4000: these are HTML bodies, and the shipped ones are already 200-400 characters before an
        // administrator adds a paragraph of their own.
        entity.Property(o => o.BodyAr).HasMaxLength(4000).IsRequired();
        entity.Property(o => o.BodyEn).HasMaxLength(4000).IsRequired();
        entity.HasIndex(o => o.Key).IsUnique();
    }
}
