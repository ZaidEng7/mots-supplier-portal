// How one bid's final scores in an evaluation map to their table.
//
// One row per evaluation and bid. The scores are fixed-precision rather than floating point, because a
// score that decides an award must not depend on how a number happened to round.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Evaluation;

internal sealed class ConsolidatedResultConfiguration : IEntityTypeConfiguration<ConsolidatedResult>
{
    public void Configure(EntityTypeBuilder<ConsolidatedResult> entity)
    {
        entity.ToTable("consolidated_result", "evaluation");
        entity.HasKey(r => r.Id);
        entity.Property(r => r.TechnicalWeightedScore).HasPrecision(8, 2);
        entity.Property(r => r.FinancialWeightedScore).HasPrecision(8, 2);
        entity.Property(r => r.WeightedTotal).HasPrecision(8, 2);
        entity.HasIndex(r => new { r.EvaluationId, r.ProposalId }).IsUnique();
    }
}
