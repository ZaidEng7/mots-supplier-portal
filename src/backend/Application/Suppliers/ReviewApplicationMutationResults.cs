using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Suppliers;

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
