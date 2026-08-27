namespace MotsSupplierPortal.Domain.Suppliers;

public enum SupplierLegalType
{
    Company,
    Individual,
    Partnership,
}

/// <summary>FR-PROF-002/DOMAIN-MODEL.md LegalInfo VO. Registration number, tax id, incorporation
/// date, and legal form captured generically - no invented Syrian validation rules
/// (docs/product/ASSUMPTIONS.md ASM-020), same pattern already used elsewhere in this codebase.
/// [ASSUMPTION / REQUIRES BUSINESS CONFIRMATION] per FEAT-04.2.</summary>
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
