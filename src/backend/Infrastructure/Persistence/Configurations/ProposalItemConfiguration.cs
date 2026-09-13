using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

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
