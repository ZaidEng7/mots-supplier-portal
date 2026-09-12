using MotsSupplierPortal.Domain.Awards;

namespace MotsSupplierPortal.Application.Awards;

public sealed record AwardApprovalDto(int StepNo, Guid? ApproverUserId, ApprovalDecision? Decision, string? Comment, DateTimeOffset? DecidedAt);

/// <summary>FEAT-14.7/FR-AWD-008: ComparisonSnapshotJson is only ever non-null once State is
/// Awarded - the frozen EPIC-12 comparison view at the moment of award, never re-queried live. This
/// same DTO/endpoint serves both "current award status" (pre-Awarded) and "the immutable award
/// file" (post-Awarded) - there is no separate endpoint, since the underlying data is identical and
/// Award's own domain methods already make everything but the ERP sync fields immutable once
/// Awarded (see Award.cs's own doc comment).</summary>
public sealed record AwardDto(
    Guid Id, string RfqReferenceCode, AwardState State,
    /// <summary>
    /// The internal identifier, still on the wire and no longer read by anything - see
    /// ConsolidatedResultDto.ProposalId and DECISIONS-TAKEN.md D-69 for why removing it waits for a
    /// major version.
    /// </summary>
    Guid WinningProposalId,
    /// <summary>T-068: the winning bid's §3 public code, which is what the award screen reads and
    /// posts back now. It used to read and post the GUID above.</summary>
    string WinningProposalCode, string JustificationAr, string JustificationEn,
    Guid RecommendedByUserId, DateTimeOffset RecommendedAt, int RecommendationRevision,
    IReadOnlyList<AwardApprovalDto> Approvals,
    DateTimeOffset? AwardedAt, string? ComparisonSnapshotJson,
    ErpSyncStatus ErpSyncStatus, string? ExternalPurchaseOrderRef, DateTimeOffset? ErpSyncedAt, int ErpRetryCount,
    // §8.1: the version this read saw, emitted as the ETag and sent back as If-Match.
    uint RowVersion);

/// <summary>
/// T-068: the winner is named by the bid's §3 PUBLIC code, not by its database identifier.
///
/// <para>The award screen used to post a GUID it had read out of a comparison response. That put an
/// internal identifier in a client's hands, in a request body, on the one action that decides who wins
/// a public tender - and §3 keeps internal identifiers out of payloads for exactly that reason. The
/// caller has the code: it is what the comparison and the consolidated results now carry.</para>
/// </summary>
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
    /// <summary>Wraps every Award domain-invariant refusal with the exact DomainException message -
    /// same pattern as RfqMutationResult.InvalidState/EvaluationMutationResult.InvalidState.</summary>
    public sealed record InvalidState(string Message) : AwardMutationResult;
    /// <summary>BRULE-073: the approver and the recommender are the same user - a distinct result
    /// from InvalidState so the API can return a specific, named error code rather than a generic
    /// domain-exception message for this particular refusal.</summary>
    public sealed record SegregationOfDutiesViolation : AwardMutationResult;
    /// <summary>BRULE-075: the winning supplier is no longer Active at approval time.</summary>
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
