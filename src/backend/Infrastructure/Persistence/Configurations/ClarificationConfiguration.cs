// How a question asked about a tender, and its answer, map to their table.
//
// The answer is optional because the row exists from the moment the question is asked. The second index
// serves a supplier looking at their own questions across tenders.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Rfqs;

internal sealed class ClarificationConfiguration : IEntityTypeConfiguration<Clarification>
{
    public void Configure(EntityTypeBuilder<Clarification> entity)
    {
        entity.ToTable("clarification", "rfq");
        entity.HasKey(c => c.Id);
        entity.Property(c => c.Question).HasMaxLength(4000).IsRequired();
        entity.Property(c => c.Answer).HasMaxLength(4000);
        entity.Property(c => c.Visibility).HasConversion<string>().HasMaxLength(20);
        entity.HasIndex(c => c.RfqId);
        entity.HasIndex(c => c.AskedBySupplierId);
    }
}
