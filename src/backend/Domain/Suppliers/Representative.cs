namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>
/// A named person authorized to act for the supplier. Exactly one is the primary
/// (supplier_admin) at all times (docs/architecture/DOMAIN-MODEL.md §5.3).
/// </summary>
public sealed class Representative
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public Guid? UserId { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public string? Position { get; set; }
    public bool IsPrimary { get; set; }
}
