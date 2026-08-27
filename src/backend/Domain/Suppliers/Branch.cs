namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>FR-PROF-005/STORY-04.5: a supplier operating location, optionally linked to one of the
/// supplier's Address[] entries.</summary>
public sealed class Branch
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public Guid? AddressId { get; set; }
    public bool IsActive { get; set; } = true;
}
