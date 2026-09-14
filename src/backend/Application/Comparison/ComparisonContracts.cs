// The vocabulary for the bid comparison: one matrix with the tender's lines down one axis and the bids
// across the other.
//
//
// WHAT IS ABSENT, AND WHY ABSENCE IS THE ANSWER
//
// Prices, totals, scores and the evaluation outcome are all optional on a bid's row, and every one of them
// is absent for the same reason: absence rather than a hidden or masked value is how this codebase already
// represents that no read path exists yet.
//
// Never a zero, never an empty list standing in for not visible, and never a placeholder word. A zero
// total is a claim about a bid; an absent one is the truth.
//
//
// THE TWO-ENVELOPE GATE
//
// Prices are only ever present for a bid that has passed technical qualification in an evaluation whose
// scores have been gathered. The gate itself lives in the handler that builds this.
//
// Scores are only ever present once those scores have been gathered too, because peer scores are
// unreadable before then. Deriving them here instead would be exactly the same breach as reading the rows
// directly.
//
// Consolidated scores are re-derived when read rather than stored separately, so there is one source for
// what a score is.
//
// The tender's own line items and the requirement answers are present from the start, because the shape of
// the matrix does not depend on the gate having opened.
//
//
// THE EVALUATION STATE IS ALWAYS THE REAL ONE
//
// Including the case where no evaluation exists at all, which is spelled out rather than omitted. The
// interface needs it to choose between three different placeholders: no bids yet, waiting for scores to be
// gathered, and the real scored matrix. It carries no evaluation content itself, so naming the state
// reveals nothing the gate is holding back.
//
//
// TIES
//
// A rank that came from a tie no rule could break is marked as such. The comparison is where an officer
// sees the ranking, so it is where the tie has to be visible and resolvable.

namespace MotsSupplierPortal.Application.Comparison;

public sealed record ComparisonRfqItemDto(Guid Id, int LineNo, string TitleAr, string TitleEn, decimal Quantity, string UnitOfMeasureCode);

public sealed record ComparisonRequirementAnswerDto(Guid RequirementId, string TextAr, string TextEn, bool IsMandatory, bool Answered);

public sealed record ComparisonItemPriceDto(Guid RfqItemId, decimal Quantity, decimal UnitPrice, decimal? Discount, decimal LineTotal);

public sealed record ComparisonCriterionScoreDto(
    Guid CriterionId, string NameAr, string NameEn, bool IsFinancial, decimal Weight, decimal MaxScore,
    decimal? Threshold, decimal AverageScore, bool? MetThreshold);

public sealed record ComparisonProposalDto(
    string ProposalReferenceCode, Guid SupplierId, string SupplierDisplayNameAr, string SupplierDisplayNameEn,
    string? CurrencyCode, string? PaymentTerms, string? IncotermCode, string? DeliveryTermsAr, string? DeliveryTermsEn,
    string? Warranty, DateOnly? ValidityEnd, DateTimeOffset SubmittedAt,
    IReadOnlyList<ComparisonRequirementAnswerDto> Requirements,
    IReadOnlyList<ComparisonItemPriceDto>? Items,
    decimal? GrandTotal,
    bool? TechnicallyQualified,
    decimal? TechnicalWeightedScore,
    decimal? FinancialWeightedScore,
    decimal? WeightedTotal,
    int? Rank,
    bool TieUnresolved,
    string? TieResolutionReason,
    IReadOnlyList<ComparisonCriterionScoreDto>? CriterionScores);

public sealed record ComparisonDto(
    string RfqReferenceCode, string RfqTitleAr, string RfqTitleEn, string EvaluationState,
    IReadOnlyList<ComparisonRfqItemDto> RfqItems,
    IReadOnlyList<ComparisonProposalDto> Proposals);

public interface IGetComparisonHandler
{
    Task<ComparisonDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}
