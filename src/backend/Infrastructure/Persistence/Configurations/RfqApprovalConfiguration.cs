// How one step of a tender's approval chain maps to its table.
//
// One row per tender and step number, so a step cannot be recorded twice.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Rfqs;

internal sealed class RfqApprovalConfiguration : IEntityTypeConfiguration<RfqApproval>
{
    public void Configure(EntityTypeBuilder<RfqApproval> entity)
    {
        entity.ToTable("rfq_approval", "rfq");
        entity.HasKey(a => a.Id);
        entity.Property(a => a.Decision).HasConversion<string>().HasMaxLength(20);
        entity.Property(a => a.Comment).HasMaxLength(2000);
        entity.HasIndex(a => new { a.RfqId, a.StepNo }).IsUnique();
    }
}
