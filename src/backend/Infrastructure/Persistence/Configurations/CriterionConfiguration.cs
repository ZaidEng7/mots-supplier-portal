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

internal sealed class CriterionConfiguration : IEntityTypeConfiguration<Criterion>
{
    public void Configure(EntityTypeBuilder<Criterion> entity)
    {
        entity.ToTable("criterion", "evaluation");
        entity.HasKey(c => c.Id);
        entity.Property(c => c.NameAr).HasMaxLength(200).IsRequired();
        entity.Property(c => c.NameEn).HasMaxLength(200).IsRequired();
        entity.Property(c => c.Dimension).HasConversion<string>().HasMaxLength(20);
        entity.Property(c => c.ScoringType).HasConversion<string>().HasMaxLength(20);
        entity.Property(c => c.Weight).HasPrecision(5, 2);
        entity.Property(c => c.MaxScore).HasPrecision(6, 2);
        entity.Property(c => c.Threshold).HasPrecision(6, 2);
        entity.Property(c => c.GuidanceAr).HasMaxLength(1000);
        entity.Property(c => c.GuidanceEn).HasMaxLength(1000);
        entity.HasIndex(c => c.EvaluationTemplateId);
    }
}
