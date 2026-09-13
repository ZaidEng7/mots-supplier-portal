using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Suppliers;

public interface IListReviewQueueHandler
{
    /// <summary>Submitted/UnderReview/Resubmitted applications - the reviewer's work queue.
    /// MSP-84: keyset-paged (see KeysetCursor for why). FEAT-03.6: state restricts to one of
    /// the three queue-eligible OnboardingStates; assignedTo accepts "me" (resolved to the
    /// caller), "unassigned", or a literal reviewer user id - null means no assignment filter.</summary>
    Task<ListEnvelope<ReviewQueueItemDto>> HandleAsync(string? cursor, int? limit, bool withCount, string? state, string? assignedTo, CancellationToken ct);
}

public interface IClaimReviewItemHandler
{
    Task<ClaimQueueItemResult> HandleAsync(string referenceCode, CancellationToken ct);
}

public interface IUnassignReviewItemHandler
{
    Task<ClaimQueueItemResult> HandleAsync(string referenceCode, CancellationToken ct);
}

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

public interface IRequestInfoHandler
{
    Task<ReviewDecisionResult> HandleAsync(RequestInfoCommand command, CancellationToken ct);
}

public interface IResubmitApplicationHandler
{
    Task<ReviewDecisionResult> HandleAsync(CancellationToken ct);
}
