// A supplier's legal identity: its registered names, its registration and tax numbers, what kind of
// legal entity it is, and when it was established.
//
// The numbers are captured generically, with no invented format rules for Syrian registration or tax
// identifiers. Nothing in the requirements states those formats, and a made-up pattern would reject
// legitimate companies. That is the same treatment every other undecided rule in this codebase gets.
//
// It is a value rather than a record with its own life: it has no identity of its own and is replaced
// wholesale rather than edited field by field.

namespace MotsSupplierPortal.Domain.Suppliers;

public enum SupplierLegalType
{
    Company,
    Individual,
    Partnership,
}

public sealed class LegalInfo
{
    public string LegalNameAr { get; private set; } = null!;
    public string LegalNameEn { get; private set; } = null!;
    public string? RegistrationNumber { get; private set; }
    public string? TaxId { get; private set; }
    public SupplierLegalType SupplierType { get; private set; }
    public DateOnly? EstablishedOn { get; private set; }

    private LegalInfo() { }

    public static LegalInfo Create(string legalNameAr, string legalNameEn, string? registrationNumber, string? taxId, SupplierLegalType supplierType, DateOnly? establishedOn) =>
        new()
        {
            LegalNameAr = legalNameAr,
            LegalNameEn = legalNameEn,
            RegistrationNumber = registrationNumber,
            TaxId = taxId,
            SupplierType = supplierType,
            EstablishedOn = establishedOn,
        };
}
