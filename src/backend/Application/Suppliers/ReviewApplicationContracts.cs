using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Suppliers;

/// <summary>FEAT-03.6/FR-ONB-012: EnteredQueueAt is when this application most recently entered
/// the reviewer's active queue (submitted, resubmitted, review resumed after info was provided,
/// or compliance-retriggered back into UnderReview) - not Supplier.CreatedAt (registration date),
/// which would make a long-registered supplier who just resubmitted read as stale. Falls back to
/// CreatedAt only if no such audit row exists (should not happen for a queue-eligible state, but a
/// row without one reads as "just entered" rather than crashing the queue).</summary>
public sealed record ReviewQueueItemDto(
    string ReferenceCode, string DisplayNameAr, string DisplayNameEn, string OnboardingState, DateTimeOffset EnteredQueueAt,
    Guid? AssignedReviewerId, string? AssignedReviewerName);

public interface IListReviewQueueHandler
{
    /// <summary>Submitted/UnderReview/Resubmitted applications - the reviewer's work queue.
    /// MSP-84: keyset-paged (see ReviewQueueCursor for why). FEAT-03.6: state restricts to one of
    /// the three queue-eligible OnboardingStates; assignedTo accepts "me" (resolved to the
    /// caller), "unassigned", or a literal reviewer user id - null means no assignment filter.</summary>
    Task<ListEnvelope<ReviewQueueItemDto>> HandleAsync(string? cursor, int? limit, bool withCount, string? state, string? assignedTo, CancellationToken ct);
}

public abstract record ClaimQueueItemResult
{
    public sealed record Success(ReviewQueueItemDto Item) : ClaimQueueItemResult;
    public sealed record NotFound : ClaimQueueItemResult;
}

public interface IClaimReviewItemHandler
{
    Task<ClaimQueueItemResult> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface IUnassignReviewItemHandler
{
    Task<ClaimQueueItemResult> HandleAsync(string referenceCode, CancellationToken ct);
}

public sealed record ReviewAnnotationDto(Guid Id, DateTimeOffset RequestedAt, string Reason, IReadOnlyList<string> FlaggedProfileFields, IReadOnlyList<string> FlaggedDocumentTypeCodes, DateTimeOffset? ResolvedAt);

/// <summary>FEAT-04.10: ERP mapping fields - read-only to staff, never exposed to the supplier
/// (see SupplierDto's own doc comment for the other half of that split).</summary>
public sealed record ErpSyncDto(string? ExternalId, string SyncStatus, DateTimeOffset? LastSyncedAt);

public sealed record ReviewerSupplierViewDto(SupplierDto Supplier, ErpSyncDto ErpSync, IReadOnlyList<DocumentTypeStatusDto> Documents, IReadOnlyList<ReviewAnnotationDto> AnnotationHistory);

public interface IGetReviewerSupplierViewHandler
{
    Task<ReviewerSupplierViewDto?> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface IGetOwnActiveAnnotationHandler
{
    /// <summary>The supplier's own view of why they're InfoRequested and what's flagged - the
    /// reviewer-side annotation history is staff-only, but the supplier needs to know what to fix.</summary>
    Task<ReviewAnnotationDto?> HandleAsync(CancellationToken ct);
}

public abstract record ReviewDecisionResult
{
    public sealed record Success(SupplierDto Supplier) : ReviewDecisionResult;
    public sealed record NotFound : ReviewDecisionResult;
    public sealed record InvalidState(string Reason) : ReviewDecisionResult;
}

public interface IPickUpApplicationHandler
{
    Task<ReviewDecisionResult> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface IApproveApplicationHandler
{
    Task<ReviewDecisionResult> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface IRejectApplicationHandler
{
    Task<ReviewDecisionResult> HandleAsync(string referenceCode, string reason, CancellationToken ct);
}

public sealed record RequestInfoCommand(string ReferenceCode, string Reason, IReadOnlyList<string> FlaggedProfileFields, IReadOnlyList<string> FlaggedDocumentTypeCodes);

public interface IRequestInfoHandler
{
    Task<ReviewDecisionResult> HandleAsync(RequestInfoCommand command, CancellationToken ct);
}

public interface IResubmitApplicationHandler
{
    Task<ReviewDecisionResult> HandleAsync(CancellationToken ct);
}
