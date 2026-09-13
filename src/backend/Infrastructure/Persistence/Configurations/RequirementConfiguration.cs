// How one line of a tender's requested items maps to its table.
//
// The envelope is stored as text, matching the way a bid's documents store theirs.
//
// The scaffolder defaulted it to a number, which would have put the same set of values in the database
// two different ways: readable in one place and an opaque position in the other. Any reordering of those
// values would then silently re-interpret every existing row on one side only.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Rfqs;

internal sealed class RequirementConfiguration : IEntityTypeConfiguration<Requirement>
{
    public void Configure(EntityTypeBuilder<Requirement> entity)
    {
        entity.ToTable("requirement", "rfq");
        entity.HasKey(q => q.Id);
        entity.Property(q => q.TextAr).HasMaxLength(2000).IsRequired();
        entity.Property(q => q.TextEn).HasMaxLength(2000).IsRequired();
        entity.Property(q => q.DocumentTypeCode).HasMaxLength(50);
        entity.Property(q => q.ExpectedEnvelope).HasConversion<string>().HasMaxLength(20);
        entity.HasIndex(q => q.RfqId);
    }
}
