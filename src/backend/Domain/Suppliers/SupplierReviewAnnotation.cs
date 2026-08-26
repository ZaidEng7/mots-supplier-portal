namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>
/// A single info-request round (FEAT-03.3): which profile fields and/or document types the
/// reviewer flagged, and why. The onboarding-editable-scope check reads the latest unresolved
/// annotation to decide which fields the supplier may currently touch.
/// </summary>
public sealed class SupplierReviewAnnotation
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public Guid RequestedByUserId { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
    public required string Reason { get; init; }
    public string[] FlaggedProfileFields { get; init; } = [];
    public Guid[] FlaggedDocumentTypeIds { get; init; } = [];
    public DateTimeOffset? ResolvedAt { get; set; }

    public bool IsResolved => ResolvedAt is not null;
}
