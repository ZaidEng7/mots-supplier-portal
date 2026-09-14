// How one priced line of a bid maps to its table.
//
// One line per bid and requested item, so a supplier cannot price the same line twice.
//
// The money and quantity columns are fixed-precision. The line total is computed from them rather than
// stored, so a stored copy cannot disagree with the figures it came from.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Proposals;

internal sealed class ProposalItemConfiguration : IEntityTypeConfiguration<ProposalItem>
{
    public void Configure(EntityTypeBuilder<ProposalItem> entity)
    {
        entity.ToTable("proposal_item", "proposal");
        entity.HasKey(i => i.Id);
        entity.Property(i => i.Quantity).HasPrecision(18, 4);
        entity.Property(i => i.UnitPrice).HasPrecision(18, 4);
        entity.Property(i => i.Discount).HasPrecision(18, 4);
        entity.Property(i => i.NotesAr).HasMaxLength(2000);
        entity.Property(i => i.NotesEn).HasMaxLength(2000);
        entity.Ignore(i => i.LineTotal);
        entity.HasIndex(i => new { i.ProposalId, i.RfqItemId }).IsUnique();
    }
}
