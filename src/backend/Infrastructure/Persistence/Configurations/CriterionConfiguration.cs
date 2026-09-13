// How one scoring criterion of a live evaluation maps to its table.
//
// The weights and scores are fixed-precision, like every other figure that feeds an award decision.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Evaluation;

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
