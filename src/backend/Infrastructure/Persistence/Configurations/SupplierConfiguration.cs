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

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> entity)
    {
        entity.ToTable("supplier", "supplier");
        // ── EPIC-20 full-text search ──────────────────────────────────────────────────────
        // A STORED GENERATED column, not a trigger. Postgres computes it on write from the columns
        // it names, so there is no trigger to keep in step with a rename and no way for the index to
        // drift from the row - which is the failure mode of every hand-maintained search column.
        //
        // 'simple' for both languages, and this is the decision the sizing flagged rather than a
        // shortcut: Postgres ships no Arabic dictionary, so no configuration stems Arabic correctly.
        // 'simple' lower-cases and splits on non-word characters and does not stem, so "contracts"
        // will not match "contract". Using 'english' on the English column and 'simple' on the Arabic
        // one would make the two halves of one search behave differently for no stated reason;
        // picking one honest behaviour and saying so beats half-stemming. Adding an Arabic dictionary
        // (hunspell, or a thesaurus) is a decision for whoever owns the database, and it is a change
        // to this expression rather than to the schema.
        //
        // The reference code is IN the vector because "find RFQ-2026-000123" is the most common thing
        // anyone types into a search box on a system like this - and the regexp_replace is what makes
        // that actually work. Postgres's parser treats "RFQ-2026-000006" as 'rfq', '-2026', '-000006':
        // it reads the hyphenated numeric parts as SIGNED INTEGERS and keeps the sign in the lexeme. A
        // query built by splitting the same string on non-alphanumerics produces 'rfq', '2026',
        // '000006', which match nothing. Caught by an integration test against a real code shape after
        // the feature worked perfectly against the letter-suffixed demo codes - RFQ-DEMO-0006 tokenises
        // differently and hid it entirely.
        //
        // Collapsing every non-alphanumeric run to a space before tokenising means the stored side and
        // SearchHandler.Tokenise follow ONE rule. That is the property worth having: the alternative is
        // two tokenisers that agree on most inputs.
        entity.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
            .HasComputedColumnSql(
                "to_tsvector('simple', regexp_replace(coalesce(\"DisplayNameAr\",'') || ' ' || coalesce(\"DisplayNameEn\",'') || ' ' || coalesce(\"ReferenceCode\",''), '[^[:alnum:]]+', ' ', 'g'))",
                stored: true);
        entity.HasIndex("SearchVector").HasMethod("GIN");
        entity.HasKey(s => s.Id);
        entity.Property(s => s.ReferenceCode).HasMaxLength(30).IsRequired();
        entity.HasIndex(s => s.ReferenceCode).IsUnique();
        entity.Property(s => s.DisplayNameAr).HasMaxLength(200).IsRequired();
        entity.Property(s => s.DisplayNameEn).HasMaxLength(200).IsRequired();
        entity.Property(s => s.Description).HasMaxLength(2000);
        entity.Property(s => s.Website).HasMaxLength(300);
        entity.Property(s => s.LogoStorageKey).HasMaxLength(500);
        entity.Property(s => s.SupplierGroup).HasMaxLength(100);
        entity.Property(s => s.CurrencyCode).HasMaxLength(3);
        entity.Property(s => s.ExternalId).HasMaxLength(100);
        entity.Property(s => s.SyncStatus).HasConversion<string>().HasMaxLength(20);
        entity.Property(s => s.TermsAcceptedVersion).HasMaxLength(20);
        entity.Property(s => s.OnboardingState).HasConversion<string>().HasMaxLength(30);
        entity.Property(s => s.LifecycleState).HasConversion<string>().HasMaxLength(30);
        entity.Property(s => s.RowVersion).IsAppManagedVersion();
        entity.HasIndex(s => s.OnboardingState);
        entity.HasMany(s => s.Representatives).WithOne().HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(s => s.Addresses).WithOne().HasForeignKey(a => a.SupplierId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(s => s.Contacts).WithOne().HasForeignKey(c => c.SupplierId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(s => s.Branches).WithOne().HasForeignKey(b => b.SupplierId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(s => s.BankAccounts).WithOne().HasForeignKey(b => b.SupplierId).OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(s => s.CategoryLinks).WithOne().HasForeignKey(l => l.SupplierId).OnDelete(DeleteBehavior.Cascade);

        entity.OwnsOne(s => s.LegalInfo, legal =>
        {
            // No explicit ToTable() here: owned types default to the owner's table already.
            // Calling ToTable() with the SAME name turns this into an explicit table-splitting
            // fragment, which makes EF emit a second UPDATE against this row (checked against
            // the same xmin concurrency token) whenever the Supplier aggregate is saved together
            // with an unrelated child-collection change (e.g. AddAddress/AddContact) - the second
            // UPDATE then finds the row's xmin already bumped by the first and throws
            // DbUpdateConcurrencyException with 0 rows affected.
            legal.Property(l => l.LegalNameAr).HasColumnName("LegalNameAr").HasMaxLength(200);
            legal.Property(l => l.LegalNameEn).HasColumnName("LegalNameEn").HasMaxLength(200);
            legal.Property(l => l.RegistrationNumber).HasColumnName("RegistrationNumber").HasMaxLength(100);
            legal.Property(l => l.TaxId).HasColumnName("TaxId").HasMaxLength(100);
            legal.Property(l => l.SupplierType).HasColumnName("SupplierType").HasConversion<string>().HasMaxLength(20);
            legal.Property(l => l.EstablishedOn).HasColumnName("EstablishedOn");
        });
        entity.Navigation(s => s.LegalInfo).IsRequired(false);
    }
}
