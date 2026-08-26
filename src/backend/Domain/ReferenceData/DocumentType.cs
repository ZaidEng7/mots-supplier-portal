namespace MotsSupplierPortal.Domain.ReferenceData;

/// <summary>
/// Configurable required-document catalog (FR-DOC-001, EPIC-21 reference data). Generic types only -
/// no invented Syrian-specific document rules (docs/product/ASSUMPTIONS.md ASM-020 pattern).
/// </summary>
public sealed class DocumentType
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
    public bool IsRequired { get; init; }
    public bool ExpiryTracked { get; init; }
    public bool IsActive { get; init; } = true;
}
