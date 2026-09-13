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

internal sealed class EvaluationTemplateConfiguration : IEntityTypeConfiguration<EvaluationTemplate>
{
    public void Configure(EntityTypeBuilder<EvaluationTemplate> entity)
    {
        entity.ToTable("evaluation_template", "evaluation");
        entity.HasKey(t => t.Id);
        entity.Property(t => t.NameAr).HasMaxLength(200).IsRequired();
        entity.Property(t => t.NameEn).HasMaxLength(200).IsRequired();
        entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        entity.Property(t => t.RowVersion).IsAppManagedVersion();
        // One version-row per (FamilyId, Version) - see EvaluationTemplate.cs's own doc
        // comment on why each version is its own row rather than one row mutating in place.
        entity.HasIndex(t => new { t.FamilyId, t.Version }).IsUnique();
        entity.HasMany(t => t.Criteria).WithOne().HasForeignKey(c => c.EvaluationTemplateId).OnDelete(DeleteBehavior.Cascade);
    }
}
