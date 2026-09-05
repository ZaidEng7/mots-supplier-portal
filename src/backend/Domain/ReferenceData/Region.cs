namespace MotsSupplierPortal.Domain.ReferenceData;

/// <summary>00-foundational-decisions.md / DATABASE-MODEL.md: Region is a standard reference-data
/// lookup table (same tier as Currency/DocumentType/Category/UnitOfMeasure/Incoterm), used by
/// Address.RegionId (STORY-04.3.1: "region from reference data").</summary>
public sealed class Region
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public bool IsActive { get; set; } = true;
}
