namespace MotsSupplierPortal.Domain.ReferenceData;

public sealed class Currency
{
    public Guid Id { get; init; }
    public required string Code { get; init; } // ISO-ish, e.g. SYP, USD
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
    public bool IsActive { get; init; } = true;
}
