namespace MotsSupplierPortal.Domain.ReferenceData;

/// <summary>MSP-54 [ASSUMPTION]: a minimal flat interim category list, built now because
/// CategoryLink is a hard requirement of EPIC-04 itself (Supplier.Submit requires >=1
/// CategoryLink) - not scope creep from EPIC-21. Flat by design (no ParentId yet); a real
/// buyer-side category tree (EPIC-21) can supersede this later without breaking CategoryLink,
/// since a flat list is a strict subset of a future tree.</summary>
public sealed class Category
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public bool IsActive { get; set; } = true;
}
