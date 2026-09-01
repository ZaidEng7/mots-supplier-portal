namespace MotsSupplierPortal.Domain.Rfqs;

/// <summary>A mandatory or optional qualifying condition/document the supplier must satisfy to
/// propose (DOMAIN-MODEL.md §5.4). DocumentTypeCode, when set, references reference.document_type
/// by code (same code-not-FK convention as RfqItem.CategoryCode).</summary>
public sealed class Requirement
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public string TextAr { get; set; } = null!;
    public string TextEn { get; set; } = null!;
    public bool IsMandatory { get; set; }
    public string? DocumentTypeCode { get; set; }
}
