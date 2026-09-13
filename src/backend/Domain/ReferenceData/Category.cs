// A category of goods or services, such as catering or transport.
//
// A supplier declares the categories they work in, and a tender's line items name one
// each. That pairing is what puts a supplier on a buyer's suggested list.
//
// The list is flat: no parent category, no tree. It exists because completing a supplier
// profile requires at least one category, so the product could not ship without it. A
// real buyer-side category tree can replace it later without breaking anything, because a
// flat list is a subset of a tree.
//
// Deactivating a category hides it from new choices and leaves every historical record
// readable. Nothing is ever deleted from a reference list.

namespace MotsSupplierPortal.Domain.ReferenceData;

public sealed class Category
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public bool IsActive { get; set; } = true;
}
