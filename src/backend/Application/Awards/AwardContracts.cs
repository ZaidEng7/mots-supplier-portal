// The vocabulary for an award: recommending a winner, routing it for approval, the decision, issuing it,
// and the handshake with the finance system.
//
// One shape serves both the current status of an award and the permanent award file, because the
// underlying data is identical and the record itself already makes everything but the finance fields
// immutable once the award is issued. There is no second read for the frozen version.
//
// ComparisonSnapshotJson is empty until the award is issued, and then holds the comparison exactly as it
// stood at that moment. It is never re-queried.
//
// The winner is named by the bid's public code. The internal identifier is still on the wire and no
// longer read by anything; removing it waits for a version change, because a request that used to work
// must keep working.
//
// That change mattered. The award screen used to post an internal identifier it had read out of a
// comparison response, which put a database key in a client's hands, in a request body, on the one
// action that decides who wins a public tender. The rule keeping internal identifiers out of payloads
// exists for exactly that, and the caller already had the code.
//
// RowVersion is the version this read saw, which travels back as the write precondition.
//
// Two refusals have their own outcomes rather than being folded into the general one, because the screen
// should name them. The approver being the same person as the recommender is the separation-of-duties
// rule. The winning supplier no longer being able to trade is checked at approval time.
//
// Everything else the domain refuses arrives as one outcome carrying the domain's own message, which is
// the pattern every other feature in this layer follows.

namespace MotsSupplierPortal.Application.Awards;

using MotsSupplierPortal.Domain.Awards;

public sealed record AwardApprovalDto(int StepNo, Guid? ApproverUserId, ApprovalDecision? Decision, string? Comment, DateTimeOffset? DecidedAt);

public sealed record AwardDto(
    Guid Id, string RfqReferenceCode, AwardState State,
    Guid WinningProposalId,
    string WinningProposalCode, string JustificationAr, string JustificationEn,
    Guid RecommendedByUserId, DateTimeOffset RecommendedAt, int RecommendationRevision,
    IReadOnlyList<AwardApprovalDto> Approvals,
    DateTimeOffset? AwardedAt, string? ComparisonSnapshotJson,
    ErpSyncStatus ErpSyncStatus, string? ExternalPurchaseOrderRef, DateTimeOffset? ErpSyncedAt, int ErpRetryCount,
    uint RowVersion);

public sealed record RecommendAwardCommand(string RfqReferenceCode, string? WinningProposalCode, Guid? WinningProposalId, string JustificationAr, string JustificationEn);
public sealed record RouteAwardForApprovalCommand(string RfqReferenceCode);
public sealed record ApproveAwardCommand(string RfqReferenceCode);
public sealed record RejectAwardCommand(string RfqReferenceCode, string Reason);
public sealed record ExecuteAwardCommand(string RfqReferenceCode);
public sealed record RetryErpSyncCommand(string RfqReferenceCode);

public abstract record AwardMutationResult
{
    public sealed record Success(AwardDto Award) : AwardMutationResult;
    public sealed record NotFoundOrOutOfScope : AwardMutationResult;
    public sealed record InvalidState(string Message) : AwardMutationResult;
    public sealed record SegregationOfDutiesViolation : AwardMutationResult;
    public sealed record SupplierNotActive : AwardMutationResult;
}

public interface IGetAwardHandler
{
    Task<AwardDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IRecommendAwardHandler
{
    Task<AwardMutationResult> HandleAsync(RecommendAwardCommand command, CancellationToken ct);
}

public interface IRouteAwardForApprovalHandler
{
    Task<AwardMutationResult> HandleAsync(RouteAwardForApprovalCommand command, CancellationToken ct);
}

public interface IApproveAwardHandler
{
    Task<AwardMutationResult> HandleAsync(ApproveAwardCommand command, CancellationToken ct);
}

public interface IRejectAwardHandler
{
    Task<AwardMutationResult> HandleAsync(RejectAwardCommand command, CancellationToken ct);
}

public interface IExecuteAwardHandler
{
    Task<AwardMutationResult> HandleAsync(ExecuteAwardCommand command, CancellationToken ct);
}

public interface IRetryErpSyncHandler
{
    Task<AwardMutationResult> HandleAsync(RetryErpSyncCommand command, CancellationToken ct);
}
