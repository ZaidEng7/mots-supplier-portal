// Feed 1 of the ministry's Syria Hotels Dashboard: one row per supplier, in the ministry's own column names.
//
// THIS IS NOT THE REGISTRY EXPORT, and the duplication between them is deliberate. The registry export is
// ours - ninety columns in our vocabulary, for the data lake, free to change whenever we have something new to
// say. This is a CONTRACT WITH SOMEBODY ELSE'S DASHBOARD: twenty-two columns whose names, order and spelling
// were specified in a workbook, read by name by a nightly job we will never see. Merging them would mean every
// change to our export risked breaking their charts, and every column they asked for would land in a file the
// lake also reads. Different owners, different reasons to change, different files.
//
// THE COLUMN NAMES ARE THEIRS, PascalCase and all - SupplierID, not supplier_id. Renaming one to match house
// style would break a chart in a building we cannot see.
//
// FOUR VALUES ARE TRANSLATED, because the two systems name the same things differently. They are here rather
// than in the query so the mapping is one block a person can read beside the workbook:
//
//   ApprovalStatus   nine onboarding states become their three. Approved is Approved; anything sitting with a
//                    reviewer is Pending Financial Approval; everything before submission, and Rejected, is
//                    Draft. Their middle name is a financial one and ours is not - what both mean is "not yet
//                    admitted, being looked at", which is the fact their dashboard groups by.
//   Disabled         1 when Suspended or Deactivated. They asked for a flag; we hold a lifecycle.
//   Country          normalised to ISO 3166 alpha-2, which their lookup sheet requires. Our column is free
//                    text and holds three spellings today: SY, syria and Türkiye.
//   Governorate      the English name rather than our code, matching their example value "Damascus".
//
// AN UNKNOWN COUNTRY PASSES THROUGH UNCHANGED rather than becoming empty or a guess. A value this list has not
// met is a gap in the list, and the way to find it is to see it in the file. An empty cell hides it; mapping
// everything unrecognised to SY would put a foreign supplier in Syria, which is the silent kind of wrong that
// gets built on before anyone notices.
//
// SUPPLIERGROUP IS THE PRIMARY CATEGORY, one value, which is what their field holds. A supplier may claim
// several categories and one of ours claims four; the primary one exists precisely so this column does not
// have to invent a rule. Before it existed the honest options were a joined list, which their chart would have
// rendered as one absurd group, or an arbitrary pick, which their chart would have rendered as a quiet lie.
//
// A SUPPLIER WITH NO ADDRESS still gets a row, with its six address columns blank. Four of them are Must
// fields in the workbook, so this is a visible hole rather than a hidden one - which is the point. A supplier
// that exists belongs in a master feed; dropping it would make the ministry's count of registered suppliers
// disagree with ours, and nothing in the file would say why.
//
// EVERY VALUE IS INVARIANT-CULTURE, dates ISO 8601, decimals with a point. The reader is a loader, not a
// person, and a file whose numbers change shape with the server's locale is the classic overnight breakage.

namespace MotsSupplierPortal.Application.Suppliers;

using System.Globalization;
using MotsSupplierPortal.Application.Exports;
using MotsSupplierPortal.Domain.Suppliers;

public sealed record MinistrySupplierFeedRecord(
    Supplier Supplier,
    IReadOnlyDictionary<string, string> CategoryNameEn,
    IReadOnlyDictionary<string, string> RegionNameEn);

public static class MinistrySupplierFeedCsv
{
    public const string FileName = "mots-feed-suppliers.csv";

    public static readonly IReadOnlyList<string> Columns =
    [
        "SupplierID", "SupplierName", "SupplierNameAr", "SupplierCode", "SupplierGroup", "ApprovalStatus",
        "Disabled", "DefaultCurrency", "RegistrationType", "CommercialRegisterNo", "TaxID", "Country",
        "AddressLine", "City", "Governorate", "Latitude", "Longitude", "Phone", "Email", "PortalUser",
        "CreatedOn", "LastModified",
    ];

    private static readonly IReadOnlyDictionary<string, string> CountryCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["syria"] = "SY",
            ["syrian arab republic"] = "SY",
            ["سوريا"] = "SY",
            ["türkiye"] = "TR",
            ["turkiye"] = "TR",
            ["turkey"] = "TR",
            ["تركيا"] = "TR",
            ["lebanon"] = "LB",
            ["لبنان"] = "LB",
            ["iraq"] = "IQ",
            ["العراق"] = "IQ",
            ["jordan"] = "JO",
            ["الأردن"] = "JO",
            ["saudi arabia"] = "SA",
            ["السعودية"] = "SA",
        };

    public static string Header => CsvFormat.Row(Columns);

    public static string Row(MinistrySupplierFeedRecord record) => CsvFormat.Row(Cells(record));

    public static IReadOnlyList<string?> Cells(MinistrySupplierFeedRecord record)
    {
        var s = record.Supplier;
        var legal = s.LegalInfo;

        var address = s.Addresses.FirstOrDefault(a => a.IsPrimary) ?? s.Addresses.FirstOrDefault();
        var representative = s.Representatives.FirstOrDefault(r => r.IsPrimary) ?? s.Representatives.FirstOrDefault();
        var primaryCategory = s.PrimaryCategoryCode;

        return
        [
            s.ReferenceCode,
            legal?.LegalNameEn ?? s.DisplayNameEn,
            legal?.LegalNameAr ?? s.DisplayNameAr,
            null,
            primaryCategory is null
                ? null
                : record.CategoryNameEn.TryGetValue(primaryCategory, out var group) ? group : primaryCategory,
            ApprovalStatus(s.OnboardingState),
            Flag(s.LifecycleState is SupplierLifecycleState.Suspended or SupplierLifecycleState.Deactivated),
            s.CurrencyCode,
            legal?.SupplierType.ToString(),
            legal?.RegistrationNumber,
            legal?.TaxId,
            address is null ? null : CountryCode(address.Country),
            address?.Line1,
            address?.City,
            address is null
                ? null
                : record.RegionNameEn.TryGetValue(address.RegionCode, out var region) ? region : address.RegionCode,
            Coordinate(address?.Latitude),
            Coordinate(address?.Longitude),
            representative?.Phone,
            representative?.Email,
            representative is null ? null : Flag(representative.UserId.HasValue),
            Date(s.CreatedAt),
            Time(s.UpdatedAt),
        ];
    }

    public static string ApprovalStatus(SupplierOnboardingState state) => state switch
    {
        SupplierOnboardingState.Approved => "Approved",
        SupplierOnboardingState.Submitted
            or SupplierOnboardingState.UnderReview
            or SupplierOnboardingState.InfoRequested
            or SupplierOnboardingState.Resubmitted => "Pending Financial Approval",
        _ => "Draft",
    };

    public static string CountryCode(string stored)
    {
        var trimmed = stored.Trim();
        if (trimmed.Length == 2) return trimmed.ToUpperInvariant();

        return CountryCodes.TryGetValue(trimmed, out var code) ? code : trimmed;
    }

    private static string Flag(bool value) => value ? "1" : "0";

    private static string? Coordinate(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

    private static string Date(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
