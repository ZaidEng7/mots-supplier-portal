// One of a supplier's addresses. A supplier may have several: a head office, a billing address and
// branch addresses.
//
// RegionCode points at reference data by code.
//
// IsPrimary marks the one to use when only one is shown.

namespace MotsSupplierPortal.Domain.Suppliers;

public enum AddressKind
{
    HeadOffice,
    Billing,
    Branch,
}

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
