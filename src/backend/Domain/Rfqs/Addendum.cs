namespace MotsSupplierPortal.Domain.Rfqs;

/// <summary>FEAT-10.4/FR-CLR-004/FR-RFQ-012, BRULE-038: the first real use of "locked after
/// Published except addenda". A published RFQ's items/requirements/basics stay locked
/// (Rfq.EnsureDraftEditable's own guard, unchanged) - an addendum is a separate, additive record
/// of a spec/timeline change, not an in-place mutation of the original content. BRULE-038 itself is
/// tagged [ASSUMPTION / REQUIRES BUSINESS CONFIRMATION] for exactly what "materially amended"
/// requires beyond notification; this entity implements the confirmed half (record + notify), not
/// a speculative diff/versioning system for the unconfirmed half.</summary>
public sealed class Addendum
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public string TitleAr { get; init; } = null!;
    public string TitleEn { get; init; } = null!;
    public string DescriptionAr { get; init; } = null!;
    public string DescriptionEn { get; init; } = null!;
    public DateTimeOffset IssuedAt { get; init; }
    public Guid IssuedByUserId { get; init; }
}
