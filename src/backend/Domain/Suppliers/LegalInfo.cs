// A supplier's legal identity: its registered names, its registration and tax numbers, what kind of
// legal entity it is, and when it was established.
//
// The numbers are captured generically, with no invented format rules for Syrian registration or tax
// identifiers. Nothing in the requirements states those formats, and a made-up pattern would reject
// legitimate companies. That is the same treatment every other undecided rule in this codebase gets.
//
// It is a value rather than a record with its own life: it has no identity of its own and is replaced
// wholesale rather than edited field by field.
//
// THE REGISTRATION TYPE - commercial, industrial - comes only from the ERP, where Seven Gates records which register
// the number belongs to. Nobody types it in the portal, so a supplier editing their own legal details carries the
// existing one across rather than losing it to a form that has no field for it - as long as the number is the same. A
// new or cleared number drops it, because the type described the number that is gone.

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
    public string? RegistrationType { get; private set; }
    public string? TaxId { get; private set; }
    public SupplierLegalType SupplierType { get; private set; }
    public DateOnly? EstablishedOn { get; private set; }

    private LegalInfo() { }

    public static LegalInfo Create(string legalNameAr, string legalNameEn, string? registrationNumber, string? taxId, SupplierLegalType supplierType, DateOnly? establishedOn, string? registrationType = null) =>
        new()
        {
            LegalNameAr = legalNameAr,
            LegalNameEn = legalNameEn,
            RegistrationNumber = registrationNumber,
            RegistrationType = registrationType,
            TaxId = taxId,
            SupplierType = supplierType,
            EstablishedOn = establishedOn,
        };
}
