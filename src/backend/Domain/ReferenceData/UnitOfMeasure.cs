// How a quantity is counted: per person, per day, per unit.
//
// A tender's line item and a supplier's catalogue entry both name one, so a price of 235
// means something specific.
//
// As with categories, this is a deliberately minimal flat list. It exists because listing
// an offering requires a unit, and a fuller buyer-side catalogue can replace it later.

namespace MotsSupplierPortal.Domain.ReferenceData;

public sealed class UnitOfMeasure
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public bool IsActive { get; set; } = true;
}
