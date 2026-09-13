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

internal sealed class RequirementConfiguration : IEntityTypeConfiguration<Requirement>
{
    public void Configure(EntityTypeBuilder<Requirement> entity)
    {
        entity.ToTable("requirement", "rfq");
        entity.HasKey(q => q.Id);
        entity.Property(q => q.TextAr).HasMaxLength(2000).IsRequired();
        entity.Property(q => q.TextEn).HasMaxLength(2000).IsRequired();
        entity.Property(q => q.DocumentTypeCode).HasMaxLength(50);
        // A-2: stored as a STRING, matching ProposalDocument.Envelope. The scaffolder defaulted this
        // to an integer, which would have put the same enum in the database two different ways -
        // readable one place and an opaque ordinal the other, and any reordering of the enum members
        // would silently re-interpret every existing row on this side only.
        entity.Property(q => q.ExpectedEnvelope).HasConversion<string>().HasMaxLength(20);
        entity.HasIndex(q => q.RfqId);
    }
}
