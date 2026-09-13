// One bid's consolidated outcome, computed when the committee's scores are gathered. One row per bid.
//
// TechnicallyQualified is the two-envelope gate's own confirmation. A bid that fails it is excluded
// from the ranking regardless of any financial score it accumulated.
//
// The scores and the rank are the result of that gathering.
//
// TieUnresolved means this result is still tied with another after every tie-break rule has been
// applied, and no person has settled it. The ranks are still assigned, because a list with no order is
// useless, but the award flow refuses to offer the top rank while any top-ranked result carries this,
// because at that point the ordering between them came from nothing a rule decided. The principle is
// to be deterministic where the rules decide and to refuse to decide where they do not.
//
// TieResolvedByUserId and TieResolutionReason record who settled a tie and why. Both are null while a
// tie is unresolved, and null when there was never a tie.

namespace MotsSupplierPortal.Domain.Evaluation;

public sealed class ConsolidatedResult
{
    public Guid Id { get; init; }
    public Guid EvaluationId { get; init; }
    public Guid ProposalId { get; init; }
    public bool TechnicallyQualified { get; internal set; }
    public decimal TechnicalWeightedScore { get; internal set; }
    public decimal? FinancialWeightedScore { get; internal set; }
    public decimal WeightedTotal { get; internal set; }
    public int? Rank { get; internal set; }

    public bool TieUnresolved { get; internal set; }

    public Guid? TieResolvedByUserId { get; internal set; }

    public string? TieResolutionReason { get; internal set; }
}
