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

internal sealed class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    public void Configure(EntityTypeBuilder<Proposal> entity)
    {
        entity.ToTable("proposal", "proposal");
        entity.HasKey(p => p.Id);
        entity.Property(p => p.ReferenceCode).HasMaxLength(30).IsRequired();
        entity.HasIndex(p => p.ReferenceCode).IsUnique();
        entity.Property(p => p.State).HasConversion<string>().HasMaxLength(30);
        entity.Property(p => p.CurrencyCode).HasMaxLength(3);
        entity.Property(p => p.PaymentTerms).HasMaxLength(500);
        entity.Property(p => p.IncotermCode).HasMaxLength(10);
        entity.Property(p => p.DeliveryTermsAr).HasMaxLength(1000);
        entity.Property(p => p.DeliveryTermsEn).HasMaxLength(1000);
        entity.Property(p => p.Warranty).HasMaxLength(500);
        entity.Property(p => p.NarrativeAr).HasMaxLength(4000);
        entity.Property(p => p.NarrativeEn).HasMaxLength(4000);
        entity.Property(p => p.WithdrawReason).HasMaxLength(2000);
        // T-064: same bound, same kind of value - a supplier's free text explaining a transition.
        entity.Property(p => p.DeclineReason).HasMaxLength(2000);
        // Same bound as WithdrawReason - both are a person's free text explaining a transition.
        entity.Property(p => p.ClarificationReason).HasMaxLength(2000);
        entity.Property(p => p.RowVersion).IsAppManagedVersion();
        // Unique per (rfq, supplier) among proposals that are NOT withdrawn.
        //
        // The unfiltered version made BUSINESS-PROCESSES.md §4.1's re-entry impossible at the
        // database level: "re-submission allowed while window open (new draft)" needs a second
        // row, and the index refused one. Narrowed rather than dropped - the rule being enforced
        // is "one LIVE proposal per supplier per RFQ", which is what uniqueness was always for;
        // a withdrawn proposal is a historical record, not a current bid, and any number of them
        // can accumulate if a supplier withdraws repeatedly within the window.
        entity.HasIndex(p => new { p.RfqId, p.SupplierId })
            .IsUnique()
            // The column name is QUOTED. This project maps to PascalCase columns, and an
            // unquoted `state` folds to lowercase in Postgres and does not exist - the first
            // version of this filter failed every migration with 42703.
            //
            // A-9 added Lapsed and Cancelled, and both belong in this exclusion for the same
            // reason Withdrawn does: they are historical records rather than current bids. A
            // supplier whose draft LAPSED on RFQ-1 must be able to bid again if that RFQ reopens
            // its window, and one whose proposal was CANCELLED with the RFQ must not be blocked
            // from a re-tender. Leaving them in would have made the index refuse the second row
            // and surface as a 500 on a perfectly legitimate submission - which is exactly how
            // the unfiltered version of this index failed the first time.
            .HasFilter("\"State\" NOT IN ('Withdrawn', 'Lapsed', 'Cancelled')");
        entity.HasIndex(p => new { p.SupplierId, p.State });
        entity.HasIndex(p => new { p.RfqId, p.State });
        entity.HasMany(p => p.Items).WithOne().HasForeignKey(i => i.ProposalId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(p => p.Documents).WithOne().HasForeignKey(d => d.ProposalId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(p => p.RequirementAnswers).WithOne().HasForeignKey(a => a.ProposalId).OnDelete(DeleteBehavior.Cascade);
    }
}
