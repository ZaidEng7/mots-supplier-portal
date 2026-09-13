// A governorate or region, used by a supplier's addresses.
//
// One of the six reference lists the ministry maintains, alongside currencies,
// categories, units of measure, document types and Incoterms. They all share the same
// shape: a code, a name in both languages, and a flag that retires an entry without
// deleting it.

namespace MotsSupplierPortal.Domain.ReferenceData;

public sealed class Region
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public bool IsActive { get; set; } = true;
}
