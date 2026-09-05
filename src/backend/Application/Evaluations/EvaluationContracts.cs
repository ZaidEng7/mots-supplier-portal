using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;

namespace MotsSupplierPortal.Application.Evaluations;

public sealed record EvaluationCriterionDto(
    Guid Id, string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight, decimal MaxScore, decimal? Threshold, ScoringType ScoringType, bool IsFinancial,
    // T-021/BRULE-061: on the evaluator's own view, so the form can mark the comment required
    // before the score is refused rather than after it.
    bool RequiresJustification = false);

/// <summary>Buyer-facing roster row - never carries a raw score (blind scoring, OQ-005/BRULE-058).</summary>
public sealed record EvaluationAssignmentDto(Guid EvaluatorUserId, DateTimeOffset AssignedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? RecusedAt, string? RecusalReason);

/// <summary>A-1: <paramref name="TieUnresolved"/> says this rank came from a tie that no rule broke.
/// The award flow refuses rank 1 while it is set, and the screen has to be able to say why.</summary>
public sealed record ConsolidatedResultDto(
    Guid ProposalId, bool TechnicallyQualified, decimal TechnicalWeightedScore, decimal? FinancialWeightedScore,
    decimal WeightedTotal, int? Rank, bool TieUnresolved = false, string? TieResolutionReason = null);

/// <summary>A-1: a person breaks a tie the rules could not, and says why. Addressed by the proposal's
/// PUBLIC code, not its GUID - §3 keeps internal identifiers out of payloads, and a caller that has
/// the comparison has the code.</summary>
public sealed record ResolveEvaluationTieCommand(string RfqReferenceCode, string ProposalCode, string Reason);

public interface IResolveEvaluationTieHandler
{
    Task<EvaluationMutationResult> HandleAsync(ResolveEvaluationTieCommand command, CancellationToken ct);
}

/// <summary>Buyer/manager-facing overview - deliberately excludes every EvaluatorScore row (see
/// EvaluationAssignmentDto's own doc comment); Results is empty until Consolidate() has run.</summary>
public sealed record EvaluationDto(
    Guid Id, Guid RfqId, string RfqReferenceCode, EvaluationState State,
    IReadOnlyList<EvaluationCriterionDto> Criteria, IReadOnlyList<EvaluationAssignmentDto> Assignments, IReadOnlyList<ConsolidatedResultDto> Results,
    // §8.1: the version this read saw, emitted as the ETag and sent back as If-Match.
    uint RowVersion);

/// <summary>One evaluator's own score for one (Proposal, Criterion) - the row-level unit blind
/// scoring is enforced against. Never returned for any evaluator other than the caller.
///
/// <para>T-068: keyed by the proposal's PUBLIC code. The GUID stays inside the domain, where
/// ScoreCriterion still takes it.</para></summary>
public sealed record MyScoreDto(string ProposalCode, Guid CriterionId, decimal RawScore, string? CommentAr, string? CommentEn, DateTimeOffset ScoredAt);

/// <summary>
/// T-067: one bid, as an assigned evaluator may see it DURING scoring.
///
/// <para><b>This is the technical envelope and nothing else.</b> No <c>ProposalItemDto</c>, no
/// currency, no payment or delivery terms, no totals - the same seal T-028 put on the buyer's
/// document gate, applied to the read an evaluator actually uses. Pricing reaches a human through
/// the comparison matrix once the evaluation is Consolidated, and through no other path.</para>
///
/// <para><b>The supplier's identity IS here, deliberately.</b> Blindness in this product is
/// evaluator-to-evaluator (ROADMAP §P7: "each scores blind (cannot see peers)"), never bidder
/// anonymity - no document asks for anonymised evaluation. And BRULE-067's recusal mechanism is
/// unusable without it: an evaluator cannot declare a conflict of interest with a supplier whose
/// name they have never been shown. Withholding it would have been a fail-closed default that
/// disabled a documented control. See DECISIONS-TAKEN.md D-19.</para>
///
/// <para><b>Documents are the Technical envelope only</b> (D-7), and only once scanned clean
/// (D-10) - an evaluator is not the person to hand an unscanned file to.</para>
/// </summary>
public sealed record EvaluatorProposalDto(
    string ProposalCode,
    /// <summary>
    /// A-8: a stable per-evaluation pseudonym - "Bidder A", «مورّد أ» - so an evaluator can refer to a
    /// bid in a comment without knowing whose it is.
    ///
    /// <para>Assigned by proposal code order within the evaluation, so it is the same label on every
    /// read and for every evaluator, which is what makes a committee discussion possible at all.</para>
    /// </summary>
    string BidderLabelAr,
    string BidderLabelEn,
    /// <summary>
    /// A-8: the supplier's identity, and it is NULL while scoring is open.
    ///
    /// <para>Revealed once the evaluation is Consolidated or Finalized, and also before the evaluator
    /// has opened scoring - that earlier window is the recusal declaration (BRULE-067), where the
    /// evaluator is shown the bidder list once, declares any conflict, and is then recused or proceeds.
    /// Nobody has to recuse themselves from a bidder they cannot see, because the declaration already
    /// happened.</para>
    ///
    /// <para>This supersedes D-19, which widened the evaluator's view to include the bidder name
    /// precisely so recusal was possible. A-8 moves recusal earlier instead, which is what makes
    /// anonymised scoring compatible with BRULE-067 rather than in conflict with it.</para>
    /// </summary>
    string? SupplierReferenceCode,
    string? SupplierDisplayNameAr,
    string? SupplierDisplayNameEn,
    string? NarrativeAr,
    string? NarrativeEn,
    IReadOnlyList<RequirementAnswerDto> RequirementAnswers,
    IReadOnlyList<EvaluatorProposalDocumentDto> Documents,
    /// <summary>This evaluator's own qualification determination for this bid - per evaluator, not
    /// global, because scoring is independent until Consolidate() runs.</summary>
    bool TechnicallyQualified);

/// <summary>A technical supporting file on a bid, listed for an assigned evaluator. Addressed by the
/// proposal's code plus this id, and downloadable through the evaluator's own gated route.</summary>
public sealed record EvaluatorProposalDocumentDto(
    Guid Id, string OriginalFileName, string ContentType, string? Caption, DateTimeOffset UploadedAt);

/// <summary>Evaluator-facing view: this evaluator's own assignment status, the criteria (with
/// IsFinancial so the UI can grey out pricing pre-qualification), the proposals this evaluator
/// scores, this evaluator's own qualification determination per proposal, and only this
/// evaluator's own MyScores - never another evaluator's.</summary>
public sealed record MyEvaluationDto(
    string RfqReferenceCode, EvaluationState State,
    // T-067: the SPECIFICATION the bids answer. An evaluator held neither rfq.read nor
    // comparison.view, so before this they could not see the requirement they were scoring against
    // any more than they could see the bid. Carried on this read rather than by widening the role,
    // so one already assignment-scoped handler stays the only door.
    string RfqTitleAr, string RfqTitleEn, string? RfqDescriptionAr, string? RfqDescriptionEn,
    IReadOnlyList<RfqItemDto> RfqItems, IReadOnlyList<RequirementDto> RfqRequirements,
    DateTimeOffset? SubmittedAt, IReadOnlyList<EvaluationCriterionDto> Criteria,
    // T-067/T-068: the bids themselves, keyed by public code. Replaces `ProposalIds` (raw GUIDs) and
    // `TechnicallyQualifiedByProposal` (a GUID-keyed dictionary) - the qualification flag now travels
    // on the bid it describes instead of in a parallel map.
    IReadOnlyList<EvaluatorProposalDto> Proposals,
    IReadOnlyList<MyScoreDto> MyScores);

public sealed record OpenEvaluationCommand(string RfqReferenceCode);
public sealed record AssignEvaluatorsCommand(string RfqReferenceCode, IReadOnlyList<Guid> EvaluatorUserIds);
public sealed record RecuseEvaluatorCommand(string RfqReferenceCode, Guid EvaluatorUserId, string Reason);
// T-068: addressed by the proposal's public code. Resolved to its GUID inside the handler, which is
// also where an unknown code becomes the same 404 as a code belonging to another RFQ.
public sealed record ScoreCriterionCommand(string RfqReferenceCode, string ProposalCode, Guid CriterionId, decimal RawScore, string? CommentAr, string? CommentEn);
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

/// <summary>
/// A-8/BRULE-067: the recusal declaration window. The evaluator sees who the bidders are ONCE, before
/// scoring, and says whether they have a conflict.
///
/// <para><c>DeclarationRequired</c> is false once they have declared or been recused, and the bidder
/// list is then empty - the window closes, which is what makes the anonymity during scoring real rather
/// than decorative.</para>
/// </summary>
public sealed record ConflictDeclarationDto(
    bool DeclarationRequired,
    IReadOnlyList<DeclarationBidderDto> Bidders);

public sealed record DeclarationBidderDto(string ProposalCode, string SupplierDisplayNameAr, string SupplierDisplayNameEn);

public sealed record DeclareConflictCommand(string RfqReferenceCode, bool HasConflict, string? Reason);

public interface IGetConflictDeclarationHandler
{
    Task<ConflictDeclarationDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IDeclareConflictHandler
{
    Task<EvaluationMutationResult> HandleAsync(DeclareConflictCommand command, CancellationToken ct);
}
