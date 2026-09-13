// A place the supplier operates from, optionally pointing at one of the supplier's addresses.

namespace MotsSupplierPortal.Domain.Suppliers;

public sealed class Branch
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public Guid? AddressId { get; set; }
    public bool IsActive { get; set; } = true;
}
