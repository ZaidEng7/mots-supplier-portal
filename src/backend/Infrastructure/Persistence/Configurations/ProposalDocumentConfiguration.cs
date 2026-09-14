// How a document attached to a bid maps to its table.
//
// The envelope says whether the file belongs to the technical or the financial half, and it is stored as
// its name for the same reason the requirement's copy is: the same value must not be readable in one
// table and an opaque number in another.
//
// The scan state is the virus scanner's verdict, and a file is not served until it says so.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Proposals;

internal sealed class ProposalDocumentConfiguration : IEntityTypeConfiguration<ProposalDocument>
{
    public void Configure(EntityTypeBuilder<ProposalDocument> entity)
    {
        entity.ToTable("proposal_document", "proposal");
        entity.Property(a => a.ScanState).HasConversion<string>().HasMaxLength(20);
        entity.Property(d => d.Envelope).HasConversion<string>().HasMaxLength(20);
        entity.HasKey(d => d.Id);
        entity.Property(d => d.StorageKey).HasMaxLength(500).IsRequired();
        entity.Property(d => d.OriginalFileName).HasMaxLength(300).IsRequired();
        entity.Property(d => d.ContentType).HasMaxLength(150).IsRequired();
        entity.Property(d => d.Caption).HasMaxLength(500);
        entity.HasIndex(d => d.ProposalId);
    }
}
