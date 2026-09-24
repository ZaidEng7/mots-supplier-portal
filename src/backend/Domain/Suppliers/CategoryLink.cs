// One category a supplier says it supplies goods or services under. The category itself is reference
// data, pointed at by code.
//
// IsPrimary marks the one a supplier would give if asked for a single answer: what they mainly supply.
// A supplier may claim up to fifty categories and the directory shows all of them, so the flag is not
// about display. It exists because other systems model this as one value - the ministry's dashboard reads
// a single SupplierGroup per supplier - and choosing for them would mean inventing a rule this product
// does not have. A supplier who supplies four things is best asked which one is the main one.
//
// Exactly one link is primary while a supplier has any categories, the same invariant Address and
// Representative already carry, enforced the same way: the first one linked becomes primary, removing
// the primary promotes another, and setting a new one clears the old.

namespace MotsSupplierPortal.Domain.Suppliers;

public sealed class CategoryLink
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public required string CategoryCode { get; init; }
    public bool IsPrimary { get; set; }
}
