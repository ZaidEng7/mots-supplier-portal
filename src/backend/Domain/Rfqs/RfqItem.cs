namespace MotsSupplierPortal.Domain.Rfqs;

/// <summary>A requested line item (DOMAIN-MODEL.md §5.4). Category/UoM referential integrity is
/// validated by the handler against a code, not a DB FK - same established convention as
/// Offering/CategoryLink (see OfferingContracts.cs's own doc comment for why).</summary>
public sealed class RfqItem
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public int LineNo { get; set; }
    public string TitleAr { get; set; } = null!;
    public string TitleEn { get; set; } = null!;
    public string? SpecificationAr { get; set; }
    public string? SpecificationEn { get; set; }
    public string CategoryCode { get; set; } = null!;
    public decimal Quantity { get; set; }
    public string UnitOfMeasureCode { get; set; } = null!;
    public bool IsUnitPrice { get; set; }
    public bool IsOptional { get; set; }
}
