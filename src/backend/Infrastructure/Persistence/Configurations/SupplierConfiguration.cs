// How a supplier company maps to its table, including the column the search box reads.
//
//
// THE SEARCH COLUMN IS COMPUTED BY THE DATABASE, NOT BY A TRIGGER
//
// The database computes it on write from the columns it names, so there is no trigger to keep in step
// with a rename and no way for the index to drift from the row. Drift is the failure mode of every
// hand-maintained search column.
//
//
// NEITHER LANGUAGE IS STEMMED, AND THAT IS A DECISION RATHER THAN A SHORTCUT
//
// The sizing flagged it. The database ships no Arabic dictionary, so no configuration stems Arabic
// correctly. The configuration used here lower-cases and splits on non-word characters and does not stem,
// so "contracts" will not match "contract".
//
// Stemming the English column while leaving the Arabic one unstemmed would make the two halves of one
// search behave differently for no stated reason. Picking one honest behaviour and saying so beats
// half-stemming. Adding an Arabic dictionary is a decision for whoever owns the database, and it is a
// change to this expression rather than to the schema.
//
//
// WHY THE REFERENCE CODE IS REWRITTEN BEFORE IT IS INDEXED
//
// The code is in the search column because "find RFQ-2026-000123" is the most common thing anyone types
// into a search box on a system like this, and the character replacement is what makes that actually
// work.
//
// The database's own parser reads RFQ-2026-000006 as three pieces and treats the hyphenated numeric parts
// as signed numbers, keeping the sign inside the indexed word. A query built by splitting the same string
// on non-alphanumeric characters produces the unsigned pieces, which match nothing.
//
// Caught by an integration test against a real code shape after the feature worked perfectly against the
// letter-suffixed demonstration codes, which tokenise differently and hid it entirely.
//
// Collapsing every run of non-alphanumeric characters to a space before tokenising means the stored side
// and the search handler follow one rule. That is the property worth having; the alternative is two
// tokenisers that agree on most inputs.
//
//
// WHY THE OWNED ADDRESS AND CONTACT TYPES NAME NO TABLE
//
// Owned types already default to the owner's table. Naming the same table explicitly turns this into a
// table-splitting fragment, which makes the mapper emit a second update against this row, checked against
// the same concurrency token, whenever the supplier is saved together with an unrelated change to one of
// its child collections.
//
// That second update then finds the row's token already bumped by the first and throws a concurrency
// failure reporting zero rows affected.

namespace MotsSupplierPortal.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Suppliers;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> entity)
    {
        entity.ToTable("supplier", "supplier");
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
