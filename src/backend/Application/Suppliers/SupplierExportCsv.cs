// The supplier registry as one CSV row per supplier, for the ministry data lake.
//
// THE FILE IS A CONTRACT, not a view. Something downstream loads it on a schedule and builds columns from the
// header, so the header is fixed here in one place and the row is built from the same list in the same order.
// A column that moves silently re-points every chart built on it, which is why Header and Row sit beside each
// other and why the test asserts they are the same length rather than eyeballing the two.
//
// ONE ROW PER SUPPLIER is what was asked for, and a supplier is not one row. It has many addresses, people,
// bank accounts, branches, offerings and documents. Each of those collapses the same way: the record the portal
// itself treats as primary, in full, beside a count of how many exist. So address_* is the primary address and
// address_count says whether that was the only one. The count is the honest part - without it a reader cannot
// tell a supplier with one address from one with five.
//
// Where there is no primary - offerings, branches, documents - the values are joined with a SEMICOLON rather
// than a comma, because a comma is the field separator and quoting a list is a worse way to say the same thing.
// The document lists are POSITIONALLY ALIGNED: doc_type_codes[i], doc_states[i] and doc_expiry_dates[i] are the
// same document. A reader splitting all three on ';' gets three equal-length arrays.
//
// THE REAL BANK ACCOUNT NUMBER IS NOT HERE. It is stored encrypted and only the masked form is mapped anywhere
// in this product - SupplierDtoMapper says so for the API, and an export to a shared lake is a weaker place to
// relax it, not a stronger one. Revealing the real number is its own audited action. Everything else the profile
// holds does travel, tax identifier and personal contact details included, because that is what was asked for;
// the decision about who may then read the file is the ministry's and is recorded in the provenance header.
//
// EVERY VALUE IS INVARIANT-CULTURE. Dates are ISO 8601 round-trip, a date without a time is yyyy-MM-dd, decimals
// use a point, and booleans are true/false. The reader is a loader, not a person, and a file whose numbers
// change shape with the server's locale is the classic overnight breakage.
//
// AN EMPTY CELL MEANS ABSENT. Never the text "null", never "N/A", never a zero standing in for "not measured" -
// the same rule the reports already follow, for the same reason: a zero is a claim and an empty cell is not.

namespace MotsSupplierPortal.Application.Suppliers;

using System.Globalization;
using MotsSupplierPortal.Application.Exports;
using MotsSupplierPortal.Domain.Suppliers;

public sealed record SupplierExportLookups(
    IReadOnlyDictionary<Guid, string> DocumentTypeCodes,
    IReadOnlyDictionary<string, string> CategoryNameEn,
    IReadOnlyDictionary<string, string> CategoryNameAr,
    IReadOnlyDictionary<string, string> RegionNameEn)
{
    public static SupplierExportLookups Empty { get; } =
        new(new Dictionary<Guid, string>(), new Dictionary<string, string>(),
            new Dictionary<string, string>(), new Dictionary<string, string>());
}

public sealed record SupplierExportRecord(
    Supplier Supplier,
    IReadOnlyList<SupplierDocument> Documents,
    IReadOnlyList<Offering> Offerings,
    IReadOnlyList<SupplierReviewAnnotation> ReviewAnnotations,
    double? ProfileCompleteness,
    IReadOnlyList<string> MissingRequiredDocumentTypeCodes);

public static class SupplierExportCsv
{
    public const string ListSeparator = ";";

    public static readonly IReadOnlyList<string> Columns =
    [
        "supplier_id", "reference_code", "display_name_ar", "display_name_en", "description", "website",
        "logo_storage_key",

        "legal_name_ar", "legal_name_en", "registration_number", "tax_id", "supplier_type", "established_on",
        "supplier_group", "currency_code",

        "onboarding_state", "lifecycle_state", "is_eligible_to_participate", "terms_accepted_version",
        "terms_accepted_at", "assigned_reviewer_id", "assigned_at",

        "external_id", "sync_status", "last_synced_at",

        "category_codes", "category_names_en", "category_names_ar", "category_count",

        "address_kind", "address_line1", "address_line2", "address_city", "address_region_code",
        "address_region_name_en", "address_country", "address_postal_code", "address_latitude",
        "address_longitude", "address_count",

        "rep_full_name", "rep_email", "rep_phone", "rep_position", "rep_has_login", "rep_count",

        "contact_full_name", "contact_email", "contact_phone", "contact_role", "contact_count",

        "bank_account_holder_name", "bank_name", "bank_branch_name", "bank_account_masked", "bank_swift_bic",
        "bank_currency_code", "bank_account_count",

        "branch_names_en", "branch_names_ar", "branch_active_count", "branch_count",

        "offering_names_en", "offering_names_ar", "offering_category_codes", "offering_unit_codes",
        "offering_price_min", "offering_price_max", "offering_currency_codes", "offering_active_count",
        "offering_count",

        "doc_type_codes", "doc_states", "doc_expiry_dates", "doc_approved_count", "doc_under_review_count",
        "doc_expiring_soon_count", "doc_expired_count", "doc_earliest_expiry", "doc_storage_keys", "doc_count",

        "review_annotation_count", "review_last_reason", "review_last_resolved_at",

        "profile_completeness", "missing_profile_fields", "missing_required_documents",

        "created_at", "updated_at", "row_version",
    ];

    public static string Header => CsvFormat.Row(Columns);

    public static string Row(SupplierExportRecord record, SupplierExportLookups lookups)
        => CsvFormat.Row(Cells(record, lookups));

    public static IReadOnlyList<string?> Cells(SupplierExportRecord record, SupplierExportLookups lookups)
    {
        var s = record.Supplier;
        var legal = s.LegalInfo;

        var primaryAddress = s.Addresses.FirstOrDefault(a => a.IsPrimary) ?? s.Addresses.FirstOrDefault();
        var primaryRep = s.Representatives.FirstOrDefault(r => r.IsPrimary) ?? s.Representatives.FirstOrDefault();
        var primaryContact = s.Contacts.FirstOrDefault();
        var defaultBank = s.BankAccounts.FirstOrDefault(b => b.IsDefault) ?? s.BankAccounts.FirstOrDefault();

        var categoryCodes = s.CategoryLinks.Select(l => l.CategoryCode).OrderBy(c => c, StringComparer.Ordinal).ToList();
        var documents = record.Documents.OrderBy(d => d.UploadedAt).ToList();
        var offerings = record.Offerings.OrderBy(o => o.CreatedAt).ToList();
        var lastAnnotation = record.ReviewAnnotations.OrderByDescending(a => a.RequestedAt).FirstOrDefault();

        var prices = offerings.Where(o => o.PriceAmount.HasValue).Select(o => o.PriceAmount!.Value).ToList();
        var expiries = documents.Where(d => d.ExpiryDate.HasValue).Select(d => d.ExpiryDate!.Value).ToList();

        return
        [
            s.Id.ToString(),
            s.ReferenceCode,
            s.DisplayNameAr,
            s.DisplayNameEn,
            s.Description,
            s.Website,
            s.LogoStorageKey,

            legal?.LegalNameAr,
            legal?.LegalNameEn,
            legal?.RegistrationNumber,
            legal?.TaxId,
            legal?.SupplierType.ToString(),
            Date(legal?.EstablishedOn),
            s.SupplierGroup,
            s.CurrencyCode,

            s.OnboardingState.ToString(),
            s.LifecycleState.ToString(),
            Bool(s.IsEligibleToParticipate),
            s.TermsAcceptedVersion,
            Time(s.TermsAcceptedAt),
            s.AssignedReviewerId?.ToString(),
            Time(s.AssignedAt),

            s.ExternalId,
            s.SyncStatus.ToString(),
            Time(s.LastSyncedAt),

            Join(categoryCodes),
            Join(categoryCodes.Select(c => Lookup(lookups.CategoryNameEn, c))),
            Join(categoryCodes.Select(c => Lookup(lookups.CategoryNameAr, c))),
            Count(categoryCodes.Count),

            primaryAddress?.Kind.ToString(),
            primaryAddress?.Line1,
            primaryAddress?.Line2,
            primaryAddress?.City,
            primaryAddress?.RegionCode,
            primaryAddress is null ? null : Lookup(lookups.RegionNameEn, primaryAddress.RegionCode),
            primaryAddress?.Country,
            primaryAddress?.PostalCode,
            Number(primaryAddress?.Latitude),
            Number(primaryAddress?.Longitude),
            Count(s.Addresses.Count),

            primaryRep?.FullName,
            primaryRep?.Email,
            primaryRep?.Phone,
            primaryRep?.Position,
            primaryRep is null ? null : Bool(primaryRep.UserId.HasValue),
            Count(s.Representatives.Count),

            primaryContact?.FullName,
            primaryContact?.Email,
            primaryContact?.Phone,
            primaryContact?.Role,
            Count(s.Contacts.Count),

            defaultBank?.AccountHolderName,
            defaultBank?.BankName,
            defaultBank?.BranchName,
            defaultBank?.MaskedAccountNumber,
            defaultBank?.SwiftBic,
            defaultBank?.CurrencyCode,
            Count(s.BankAccounts.Count),

            Join(s.Branches.Select(b => b.NameEn)),
            Join(s.Branches.Select(b => b.NameAr)),
            Count(s.Branches.Count(b => b.IsActive)),
            Count(s.Branches.Count),

            Join(offerings.Select(o => o.NameEn)),
            Join(offerings.Select(o => o.NameAr)),
            Join(offerings.Select(o => o.CategoryCode)),
            Join(offerings.Select(o => o.UnitOfMeasureCode)),
            prices.Count == 0 ? null : Decimal(prices.Min()),
            prices.Count == 0 ? null : Decimal(prices.Max()),
            Join(offerings.Where(o => o.CurrencyCode is not null).Select(o => o.CurrencyCode!).Distinct()),
            Count(offerings.Count(o => o.IsActive)),
            Count(offerings.Count),

            Join(documents.Select(d => Lookup(lookups.DocumentTypeCodes, d.DocumentTypeId))),
            Join(documents.Select(d => d.State.ToString())),
            Join(documents.Select(d => Date(d.ExpiryDate) ?? "")),
            Count(documents.Count(d => d.State == DocumentState.Approved)),
            Count(documents.Count(d => d.State == DocumentState.UnderReview)),
            Count(documents.Count(d => d.State == DocumentState.ExpiringSoon)),
            Count(documents.Count(d => d.State == DocumentState.Expired)),
            expiries.Count == 0 ? null : Date(expiries.Min()),
            Join(documents.Select(d => d.StorageKey)),
            Count(documents.Count),

            Count(record.ReviewAnnotations.Count),
            lastAnnotation?.Reason,
            Time(lastAnnotation?.ResolvedAt),

            record.ProfileCompleteness is null
                ? null
                : record.ProfileCompleteness.Value.ToString("0.00", CultureInfo.InvariantCulture),
            Join(s.GetMissingProfileFields()),
            Join(record.MissingRequiredDocumentTypeCodes),

            Time(s.CreatedAt),
            Time(s.UpdatedAt),
            s.RowVersion.ToString(CultureInfo.InvariantCulture),
        ];
    }

    private static string Lookup(IReadOnlyDictionary<string, string> map, string key)
        => map.TryGetValue(key, out var value) ? value : key;

    private static string Lookup(IReadOnlyDictionary<Guid, string> map, Guid key)
        => map.TryGetValue(key, out var value) ? value : key.ToString();

    private static string? Join(IEnumerable<string> values)
    {
        var joined = string.Join(ListSeparator, values);
        return joined.Length == 0 ? null : joined;
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string? Time(DateTimeOffset? value)
        => value?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

    private static string Decimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
