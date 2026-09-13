// One evaluator's score for one criterion on one bid. Unique per evaluation, evaluator, bid and
// criterion.
//
// This row is the unit that blind scoring is enforced against: while the evaluation is in progress or
// awaiting consolidation, every read filters to the signed-in evaluator's own rows, so no evaluator
// can see another's scores before they are gathered.

namespace MotsSupplierPortal.Domain.Evaluation;

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
