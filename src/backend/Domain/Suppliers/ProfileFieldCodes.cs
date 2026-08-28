namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>
/// The canonical vocabulary a reviewer may flag when requesting information (STORY-03.3.1), and
/// the same vocabulary the supplier's edit restriction is enforced against.
///
/// This exists because there wasn't one. Before 2026-08-28 the flagged-field list was free text:
/// <c>RequestInfoRequestValidator</c> accepted any string, and the two UIs had independently
/// invented different sets - the reviewer offered
/// <c>registrationNumber, taxId, addressLine, city, country, currencyCode, primaryContactPhone</c>
/// while the supplier screen tested
/// <c>description, legalInfo, primaryContactPhone, supplierGroup, website</c>. They overlapped on
/// exactly one code, so flagging "registrationNumber" left the supplier's legal-info section
/// disabled and the supplier unable to fix the very thing they were asked to fix.
///
/// Each code names one mutation surface, so a flag maps unambiguously onto what the supplier is
/// allowed to change. Granular reviewer concepts collapse into the handler that owns them
/// (registrationNumber/taxId -> <see cref="LegalInfo"/>; addressLine/city/country ->
/// <see cref="Address"/>), because authorization has to be enforced at the boundary that actually
/// exists.
/// </summary>
public static class ProfileFieldCodes
{
    // Core profile - individually flaggable now that PATCH applies only the fields it is sent.
    public const string Description = "description";
    public const string Website = "website";
    public const string SupplierGroup = "supplierGroup";
    public const string CurrencyCode = "currencyCode";
    public const string PrimaryContactPhone = "primaryContactPhone";

    /// <summary>Whole LegalInfo value object: names, registration number, tax id, type, date.</summary>
    public const string LegalInfo = "legalInfo";

    public const string Address = "address";
    public const string Contact = "contact";
    public const string Representative = "representative";
    public const string Branch = "branch";
    public const string BankAccount = "bankAccount";
    public const string CategoryLink = "categoryLink";
    public const string Logo = "logo";

    public static readonly IReadOnlyList<string> All =
    [
        Description, Website, SupplierGroup, CurrencyCode, PrimaryContactPhone,
        LegalInfo, Address, Contact, Representative, Branch, BankAccount, CategoryLink, Logo,
    ];

    public static bool IsKnown(string code) => All.Contains(code, StringComparer.Ordinal);
}
