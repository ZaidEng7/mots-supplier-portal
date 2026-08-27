namespace MotsSupplierPortal.Domain.Configuration;

/// <summary>The category a SupplierFieldConfig row governs.</summary>
public static class FieldConfigCategory
{
    /// <summary>FEAT-04.9: which Supplier fields re-trigger review (Approved -> UnderReview) when
    /// edited. FieldCode matches the values ComplianceReTrigger already logs: legalInfo,
    /// bankAccount, categoryLink.</summary>
    public const string ComplianceRetrigger = "ComplianceRetrigger";

    /// <summary>FEAT-04.2: which LegalInfo fields are required on UpdateLegalInfo. FieldCode
    /// matches LegalInfo's own property names: legalNameAr, legalNameEn, registrationNumber,
    /// taxId, supplierType, establishedOn.</summary>
    public const string LegalInfoRequired = "LegalInfoRequired";
}

/// <summary>Admin-editable configuration, one row per (Category, FieldCode) pair - replaces what
/// used to be hardcoded call sites/validator rules for FEAT-04.9's compliance re-trigger field
/// list and FEAT-04.2's LegalInfo requiredness, per product-owner decision 2026-08-27 to make both
/// genuinely configurable rather than just documented as such.</summary>
public sealed class SupplierFieldConfig
{
    public Guid Id { get; init; }
    public required string Category { get; init; }
    public required string FieldCode { get; init; }
    public bool IsEnabled { get; set; }
}
