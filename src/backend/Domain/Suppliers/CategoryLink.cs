namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>FR-PROF-007/STORY-04.7.1: links a supplier to a category it provides goods/services
/// under (Domain.ReferenceData.Category - MSP-54's minimal flat interim list).</summary>
public sealed class CategoryLink
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public required string CategoryCode { get; init; }
}
