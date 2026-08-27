namespace MotsSupplierPortal.Domain.Suppliers;

public enum AddressKind
{
    HeadOffice,
    Billing,
    Branch,
}

/// <summary>FR-PROF-003/STORY-04.3.1: a supplier may have multiple addresses (HQ/billing/branch),
/// region sourced from reference data.</summary>
public sealed class Address
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public AddressKind Kind { get; set; }
    public required string Line1 { get; set; }
    public string? Line2 { get; set; }
    public required string City { get; set; }
    public required string RegionCode { get; set; }
    public required string Country { get; set; }
    public string? PostalCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsPrimary { get; set; }
}
