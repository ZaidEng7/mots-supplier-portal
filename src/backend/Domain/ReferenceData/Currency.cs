namespace MotsSupplierPortal.Domain.ReferenceData;

public sealed class Currency
{
    public Guid Id { get; init; }
    public required string Code { get; init; } // ISO-ish, e.g. SYP, USD
    public required string NameAr { get; set; }
    public required string NameEn { get; set; }
    public bool IsActive { get; set; } = true;
}
