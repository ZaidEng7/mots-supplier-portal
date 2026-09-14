// What the reviewer's reads and writes are called.
//
// The queue serves the three states a case can be waiting in, and is paged by cursor like every other growing
// list.
//
// Its assignee filter accepts a word meaning the caller themselves, a word meaning nobody, or a particular
// reviewer. Absent means no assignment filter at all.
//
// The supplier's own view of an open information request exists because the reviewer-side history is staff-only
// and the supplier still needs to know what to fix.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Application.Common;

public interface IListReviewQueueHandler
{
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
