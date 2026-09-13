// How a frozen scoring criterion maps to its table.
//
// The guidance column is the same length as the guidance on the template it was copied from. A shorter
// column here would truncate an instruction the template's author was allowed to write.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Evaluation;

internal sealed class EvaluationCriterionSnapshotConfiguration : IEntityTypeConfiguration<EvaluationCriterionSnapshot>
{
    public void Configure(EntityTypeBuilder<EvaluationCriterionSnapshot> entity)
    {
        entity.ToTable("evaluation_criterion_snapshot", "evaluation");
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
        entity.Ignore(c => c.IsFinancial);
        entity.HasIndex(c => c.EvaluationId);
    }
}
