// One category a supplier says it supplies goods or services under. The category itself is reference
// data, pointed at by code.

namespace MotsSupplierPortal.Domain.Suppliers;

public sealed class CategoryLink
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public required string CategoryCode { get; init; }
}
