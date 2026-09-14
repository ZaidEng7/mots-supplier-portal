// How one evaluator's score for one bid against one criterion maps to its table.
//
// The first index is the whole of that sentence, and it is unique: one evaluator gives one score per bid
// per criterion, and a second row would be a second opinion silently averaged in.
//
// The second index serves the common read, which is one evaluator's own sheet for one evaluation.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Evaluation;

internal sealed class EvaluatorScoreConfiguration : IEntityTypeConfiguration<EvaluatorScore>
{
    public void Configure(EntityTypeBuilder<EvaluatorScore> entity)
    {
        entity.ToTable("evaluator_score", "evaluation");
        entity.HasKey(s => s.Id);
        entity.Property(s => s.RawScore).HasPrecision(6, 2);
        entity.Property(s => s.CommentAr).HasMaxLength(2000);
        entity.Property(s => s.CommentEn).HasMaxLength(2000);
        entity.HasIndex(s => new { s.EvaluationId, s.EvaluatorUserId, s.ProposalId, s.CriterionId }).IsUnique();
        entity.HasIndex(s => new { s.EvaluationId, s.EvaluatorUserId });
    }
}
