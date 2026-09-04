using MotsSupplierPortal.Domain.Evaluation;

namespace MotsSupplierPortal.Application.Evaluations;

public sealed record EvaluationCriterionDto(
    Guid Id, string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight, decimal MaxScore, decimal? Threshold, ScoringType ScoringType, bool IsFinancial,
    // T-021/BRULE-061: on the evaluator's own view, so the form can mark the comment required
    // before the score is refused rather than after it.
    bool RequiresJustification = false);

/// <summary>Buyer-facing roster row - never carries a raw score (blind scoring, OQ-005/BRULE-058).</summary>
public sealed record EvaluationAssignmentDto(Guid EvaluatorUserId, DateTimeOffset AssignedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? RecusedAt, string? RecusalReason);

public sealed record ConsolidatedResultDto(Guid ProposalId, bool TechnicallyQualified, decimal TechnicalWeightedScore, decimal? FinancialWeightedScore, decimal WeightedTotal, int? Rank);

/// <summary>Buyer/manager-facing overview - deliberately excludes every EvaluatorScore row (see
/// EvaluationAssignmentDto's own doc comment); Results is empty until Consolidate() has run.</summary>
public sealed record EvaluationDto(
    Guid Id, Guid RfqId, string RfqReferenceCode, EvaluationState State,
    IReadOnlyList<EvaluationCriterionDto> Criteria, IReadOnlyList<EvaluationAssignmentDto> Assignments, IReadOnlyList<ConsolidatedResultDto> Results,
    // §8.1: the version this read saw, emitted as the ETag and sent back as If-Match.
    uint RowVersion);

/// <summary>One evaluator's own score for one (Proposal, Criterion) - the row-level unit blind
/// scoring is enforced against. Never returned for any evaluator other than the caller.</summary>
public sealed record MyScoreDto(Guid ProposalId, Guid CriterionId, decimal RawScore, string? CommentAr, string? CommentEn, DateTimeOffset ScoredAt);

/// <summary>Evaluator-facing view: this evaluator's own assignment status, the criteria (with
/// IsFinancial so the UI can grey out pricing pre-qualification), the proposals this evaluator
/// scores, this evaluator's own qualification determination per proposal, and only this
/// evaluator's own MyScores - never another evaluator's.</summary>
public sealed record MyEvaluationDto(
    Guid Id, Guid RfqId, string RfqReferenceCode, EvaluationState State,
    DateTimeOffset? SubmittedAt, IReadOnlyList<EvaluationCriterionDto> Criteria,
    IReadOnlyList<Guid> ProposalIds, IReadOnlyDictionary<Guid, bool> TechnicallyQualifiedByProposal,
    IReadOnlyList<MyScoreDto> MyScores);

public sealed record OpenEvaluationCommand(string RfqReferenceCode);
public sealed record AssignEvaluatorsCommand(string RfqReferenceCode, IReadOnlyList<Guid> EvaluatorUserIds);
public sealed record RecuseEvaluatorCommand(string RfqReferenceCode, Guid EvaluatorUserId, string Reason);
public sealed record ScoreCriterionCommand(string RfqReferenceCode, Guid ProposalId, Guid CriterionId, decimal RawScore, string? CommentAr, string? CommentEn);
public sealed record SubmitEvaluatorCommand(string RfqReferenceCode);
public sealed record ConsolidateEvaluationCommand(string RfqReferenceCode);
public sealed record FinalizeEvaluationCommand(string RfqReferenceCode);
public sealed record ReopenEvaluationCommand(string RfqReferenceCode, string Reason);

public abstract record EvaluationMutationResult
{
    public sealed record Success(EvaluationDto Evaluation) : EvaluationMutationResult;
    public sealed record NotFoundOrOutOfScope : EvaluationMutationResult;
    public sealed record InvalidState(string Message) : EvaluationMutationResult;
}

/// <summary>Deliberately not reusing EvaluationMutationResult - see SupplierRfqResult's own doc
/// comment on why a self-service result never shares a type with the buyer-side one: an evaluator
/// who is not assigned to this evaluation must get the same 404 shape as one that does not exist,
/// never a shape that could leak whether it exists.</summary>
public abstract record MyEvaluationResult
{
    public sealed record Success(MyEvaluationDto Evaluation) : MyEvaluationResult;
    public sealed record NotFoundOrNotAssigned : MyEvaluationResult;
    public sealed record InvalidState(string Message) : MyEvaluationResult;
}

public interface IOpenEvaluationHandler
{
    Task<EvaluationMutationResult> HandleAsync(OpenEvaluationCommand command, CancellationToken ct);
}

public interface IGetEvaluationHandler
{
    Task<EvaluationDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IAssignEvaluatorsHandler
{
    Task<EvaluationMutationResult> HandleAsync(AssignEvaluatorsCommand command, CancellationToken ct);
}

public interface IRecuseEvaluatorHandler
{
    Task<EvaluationMutationResult> HandleAsync(RecuseEvaluatorCommand command, CancellationToken ct);
}

public interface IConsolidateEvaluationHandler
{
    Task<EvaluationMutationResult> HandleAsync(ConsolidateEvaluationCommand command, CancellationToken ct);
}

public interface IFinalizeEvaluationHandler
{
    Task<EvaluationMutationResult> HandleAsync(FinalizeEvaluationCommand command, CancellationToken ct);
}

public interface IReopenEvaluationHandler
{
    Task<EvaluationMutationResult> HandleAsync(ReopenEvaluationCommand command, CancellationToken ct);
}

public interface IGetMyEvaluationHandler
{
    Task<MyEvaluationResult> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IScoreCriterionHandler
{
    Task<MyEvaluationResult> HandleAsync(ScoreCriterionCommand command, CancellationToken ct);
}

public interface ISubmitEvaluatorHandler
{
    Task<MyEvaluationResult> HandleAsync(SubmitEvaluatorCommand command, CancellationToken ct);
}
