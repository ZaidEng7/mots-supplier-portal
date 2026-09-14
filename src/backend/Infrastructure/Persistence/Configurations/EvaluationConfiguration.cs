// How an evaluation maps to its table.
//
// One evaluation per tender, enforced by a unique index.
//
// Deleting an evaluation deletes its criteria, its panel, its scores and its results. All four are parts
// of the evaluation rather than records that outlive it, which is what makes the frozen criteria a
// snapshot of this evaluation and not a pointer into reference data that may have moved since.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

internal sealed class EvaluationConfiguration : IEntityTypeConfiguration<MotsSupplierPortal.Domain.Evaluation.Evaluation>
{
    public void Configure(EntityTypeBuilder<MotsSupplierPortal.Domain.Evaluation.Evaluation> entity)
    {
        entity.ToTable("evaluation", "evaluation");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.State).HasConversion<string>().HasMaxLength(20);
        entity.Property(e => e.RowVersion).IsAppManagedVersion();
        entity.HasIndex(e => e.RfqId).IsUnique();
        entity.HasMany(e => e.Criteria).WithOne().HasForeignKey(c => c.EvaluationId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(e => e.Assignments).WithOne().HasForeignKey(a => a.EvaluationId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(e => e.Scores).WithOne().HasForeignKey(s => s.EvaluationId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(e => e.Results).WithOne().HasForeignKey(r => r.EvaluationId).OnDelete(DeleteBehavior.Cascade);
    }
}
