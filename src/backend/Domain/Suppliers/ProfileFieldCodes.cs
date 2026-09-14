// The list of things a reviewer may flag when asking a supplier for more information, and the same list
// the supplier's editing restriction is checked against.
//
// It exists because there was not one. The flagged-field list used to be free text: the request
// validator accepted any string at all, and the two screens had independently invented different sets.
// The reviewer's screen offered registration number, tax identifier, address line, city, country,
// currency and primary contact phone. The supplier's screen tested description, legal information,
// primary contact phone, supplier group and website. They overlapped on exactly one code, so flagging
// the registration number left the supplier's legal-information section disabled and the supplier
// unable to fix the very thing they had been asked to fix.
//
// Each code names one place a supplier can actually save something, so a flag maps unambiguously onto
// what they are allowed to change. Fine-grained reviewer concepts collapse into the save that owns
// them: registration number and tax identifier become legal information, and address line, city and
// country become the address. Permission has to be enforced at a boundary that exists.
//
// The first five are individually flaggable because the profile save applies only the fields it is
// actually sent. LegalInfo covers the whole legal identity: both names, the registration number, the
// tax identifier, the entity type and the establishment date.

namespace MotsSupplierPortal.Domain.Suppliers;

public static class ProfileFieldCodes
{
    public const string Description = "description";
    public const string Website = "website";
    public const string SupplierGroup = "supplierGroup";
    public const string CurrencyCode = "currencyCode";
    public const string PrimaryContactPhone = "primaryContactPhone";

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
