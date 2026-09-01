namespace MotsSupplierPortal.Domain.Evaluation;

/// <summary>FEAT-11.3/FR-EVL-003/004, DATABASE-MODEL.md §2.6: one evaluator's score for one
/// (Proposal, Criterion) pair - unique(EvaluationId, EvaluatorUserId, ProposalId, CriterionId).
/// This is the row-level unit blind scoring (OQ-005/BRULE-058) is enforced against: every read
/// path in this build filters to `EvaluatorUserId == scope.UserId` while the evaluation is
/// InProgress/EvaluatorSubmitted - see GetMyScoresHandler's own doc comment for the actual query.</summary>
public sealed class EvaluatorScore
{
    public Guid Id { get; init; }
    public Guid EvaluationId { get; init; }
    public Guid EvaluatorUserId { get; init; }
    public Guid ProposalId { get; init; }
    public Guid CriterionId { get; init; }
    public decimal RawScore { get; internal set; }
    public string? CommentAr { get; internal set; }
    public string? CommentEn { get; internal set; }
    public DateTimeOffset ScoredAt { get; internal set; }
}
