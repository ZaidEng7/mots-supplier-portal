// One round of a reviewer asking a supplier for something: which profile fields and which document
// types were flagged, and why.
//
// While the supplier is being reviewed, the latest unresolved annotation is what decides which fields
// the supplier is currently allowed to touch. That is why the flagged lists are stored rather than just
// the reason.

namespace MotsSupplierPortal.Domain.Suppliers;

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
