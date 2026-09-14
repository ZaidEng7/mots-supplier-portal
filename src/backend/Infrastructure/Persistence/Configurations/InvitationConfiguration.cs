// How an invitation to bid maps to its table.
//
// One invitation per tender and supplier, so a supplier cannot be invited twice to the same tender. The
// second index serves a supplier reading their own invitations.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Rfqs;

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> entity)
    {
        entity.ToTable("invitation", "rfq");
        entity.HasKey(i => i.Id);
        entity.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        entity.Property(i => i.DeclineReason).HasMaxLength(2000);
        entity.HasIndex(i => new { i.RfqId, i.SupplierId }).IsUnique();
        entity.HasIndex(i => i.SupplierId);
    }
}
