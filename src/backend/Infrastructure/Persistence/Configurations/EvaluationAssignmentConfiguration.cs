// How an evaluator's place on a panel maps to its table.
//
// One row per evaluation and evaluator, so the same person cannot be seated on one panel twice.
//
// Whether the seat is still active is computed from the recusal, so it is not stored. A stored copy would
// be a second answer to a question the row already answers.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Evaluation;

internal sealed class EvaluationAssignmentConfiguration : IEntityTypeConfiguration<EvaluationAssignment>
{
    public void Configure(EntityTypeBuilder<EvaluationAssignment> entity)
    {
        entity.ToTable("evaluation_assignment", "evaluation");
        entity.HasKey(a => a.Id);
        entity.Property(a => a.RecusalReason).HasMaxLength(2000);
        entity.Ignore(a => a.IsActive);
        entity.HasIndex(a => new { a.EvaluationId, a.EvaluatorUserId }).IsUnique();
    }
}
