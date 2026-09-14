// How one step of an award's approval chain maps to its table.
//
// The index is on the award and the step number together, because a chain is always read in order for one
// award.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Awards;

internal sealed class ApprovalConfiguration : IEntityTypeConfiguration<Approval>
{
    public void Configure(EntityTypeBuilder<Approval> entity)
    {
        entity.ToTable("approval", "award");
        entity.HasKey(a => a.Id);
        entity.Property(a => a.Decision).HasConversion<string>().HasMaxLength(20);
        entity.Property(a => a.Comment).HasMaxLength(2000);
        entity.HasIndex(a => new { a.AwardId, a.StepNo });
    }
}
