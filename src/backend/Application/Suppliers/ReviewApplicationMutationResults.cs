// What claiming a queue item and deciding an application can answer.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Application.Common;

public abstract record ClaimQueueItemResult
{
    public sealed record Success(ReviewQueueItemDto Item) : ClaimQueueItemResult;
    public sealed record NotFound : ClaimQueueItemResult;
}

public abstract record ReviewDecisionResult
{
    public sealed record Success(SupplierDto Supplier) : ReviewDecisionResult;
    public sealed record NotFound : ReviewDecisionResult;
    public sealed record InvalidState(string Reason) : ReviewDecisionResult;
}
