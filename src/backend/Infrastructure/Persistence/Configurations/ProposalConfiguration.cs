// How a bid maps to its table, and the uniqueness rule that lets a supplier bid again.
//
// The two free-text columns share one bound, because both are a person's own explanation of a transition.
//
//
// ONE LIVE BID PER SUPPLIER PER TENDER, NOT ONE BID EVER
//
// The unfiltered version of this index made the written process's re-entry impossible at the database
// level. Re-submission while the window is open means a new draft, which needs a second row, and the
// index refused one.
//
// So it was narrowed rather than dropped. The rule being enforced is one live bid per supplier per
// tender, which is what uniqueness was always for. A withdrawn bid is a historical record rather than a
// current one, and any number of them can accumulate if a supplier withdraws repeatedly inside the
// window.
//
// Two later states belong in the same exclusion for the same reason. A supplier whose draft lapsed must
// be able to bid again if that tender reopens its window, and one whose bid was cancelled along with the
// tender must not be blocked from a re-tender. Leaving them in would have made the index refuse the
// second row and surface as a server error on a perfectly legitimate submission, which is exactly how
// the unfiltered version failed the first time.
//
//
// WHY THE COLUMN NAME INSIDE THE FILTER IS QUOTED
//
// This project maps to capitalised column names, and an unquoted name folds to lower case in the
// database and then does not exist. The first version of this filter failed every migration with an
// undefined-column error.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Proposals;

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
        entity.Property(p => p.DeclineReason).HasMaxLength(2000);
        entity.Property(p => p.ClarificationReason).HasMaxLength(2000);
        entity.Property(p => p.RowVersion).IsAppManagedVersion();
        entity.HasIndex(p => new { p.RfqId, p.SupplierId })
            .IsUnique()
            .HasFilter("\"State\" NOT IN ('Withdrawn', 'Lapsed', 'Cancelled')");
        entity.HasIndex(p => new { p.SupplierId, p.State });
        entity.HasIndex(p => new { p.RfqId, p.State });
        entity.HasMany(p => p.Items).WithOne().HasForeignKey(i => i.ProposalId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(p => p.Documents).WithOne().HasForeignKey(d => d.ProposalId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(p => p.RequirementAnswers).WithOne().HasForeignKey(a => a.ProposalId).OnDelete(DeleteBehavior.Cascade);
    }
}
