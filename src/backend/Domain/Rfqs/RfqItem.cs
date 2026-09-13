// One line the tender is asking for: what it is, how much of it, and in what unit.
//
// CategoryCode and UnitOfMeasureCode point at reference data by code rather than by a database link.
// The handler checks that the code exists and is active. That is the same convention every other
// reference to a code follows in this project.
//
// IsUnitPrice says whether the supplier prices per unit or for the line as a whole. IsOptional says
// whether the supplier has to price it at all.

namespace MotsSupplierPortal.Domain.Rfqs;

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
