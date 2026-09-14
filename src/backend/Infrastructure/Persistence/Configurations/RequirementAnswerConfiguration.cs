// How a supplier's answer to one of a tender's requirements maps to its table.
//
// One answer per bid and requirement, so a second answer cannot sit beside the first with no way to tell
// which one the panel read.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Proposals;

internal sealed class RequirementAnswerConfiguration : IEntityTypeConfiguration<RequirementAnswer>
{
    public void Configure(EntityTypeBuilder<RequirementAnswer> entity)
    {
        entity.ToTable("requirement_answer", "proposal");
        entity.HasKey(a => a.Id);
        entity.Property(a => a.AnswerAr).HasMaxLength(4000).IsRequired();
        entity.Property(a => a.AnswerEn).HasMaxLength(4000).IsRequired();
        entity.HasIndex(a => new { a.ProposalId, a.RequirementId }).IsUnique();
    }
}
