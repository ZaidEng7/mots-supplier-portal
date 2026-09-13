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
