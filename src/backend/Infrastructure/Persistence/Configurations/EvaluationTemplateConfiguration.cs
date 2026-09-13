// How a scoring template maps to its table.
//
// One row per family and version together, because each version of a template is its own row rather than
// one row changing in place. That is what makes a template referenced by a live tender immutable.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Evaluation;

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
        entity.HasIndex(t => new { t.FamilyId, t.Version }).IsUnique();
        entity.HasMany(t => t.Criteria).WithOne().HasForeignKey(c => c.EvaluationTemplateId).OnDelete(DeleteBehavior.Cascade);
    }
}
