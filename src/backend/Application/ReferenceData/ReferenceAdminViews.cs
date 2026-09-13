// What one reference-data row looks like to the administrator who maintains it.
//
// Six tables share this one shape: categories, document types, currencies, units of measure, regions and
// delivery terms. They were seed-only, or in the case of delivery terms absent altogether, so a ministry
// could not add a document type without a deployment.
//
// One shape for all six rather than six near-identical surfaces. They differ only in which extra flags
// they carry, and document types are the only ones with any. A contract per table would be six copies of
// the same four operations, and the sixth copy is where somebody forgets the audit entry.
//
// The delivery terms were recorded as missing rather than invented, on the grounds that a code list
// nobody had supplied is not reference data. What settled it is that the list is not the ministry's to
// supply: it is an international standard with eleven terms, and all eleven are seeded. The ministry's
// decision is which of them a bid may quote, and that decision is recorded by deactivating the rest.
//
//
// THE AWARD-CRITICAL FLAG
//
// It reaches the wire here for the first time, and that closes half of a rule that had never been able to
// fire.
//
// Expiry of an award-critical document suspends the supplier. The column existed, the expiry job read it,
// and no seeded type set it. Nor could anybody set it, because it was absent from this shape and from the
// screen, so a database migration was the only way in.
//
// The values themselves are not changed here. Which document types are award-critical is a ministry
// decision about procurement risk, and "was suspended for a fortnight" is not undone by reactivating
// somebody. This closes the code half so a ministry that has decided can record it; the decision stays
// theirs.

namespace MotsSupplierPortal.Application.ReferenceData;

public sealed record ReferenceItemDto(
    string Code, string NameAr, string NameEn, bool IsActive,
    bool? IsRequired = null, bool? ExpiryTracked = null,
    bool? IsAwardCritical = null);

public static class ReferenceTables
{
    public const string Categories = "categories";
    public const string DocumentTypes = "document-types";
    public const string Currencies = "currencies";
    public const string UnitsOfMeasure = "units-of-measure";
    public const string Regions = "regions";
    public const string Incoterms = "incoterms";

    public static readonly string[] All =
        [Categories, DocumentTypes, Currencies, UnitsOfMeasure, Regions, Incoterms];
}

public sealed record DocumentTypeCategoryLinksDto(string DocumentTypeCode, IReadOnlyList<string> CategoryCodes);
