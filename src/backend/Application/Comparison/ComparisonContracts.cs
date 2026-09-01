namespace MotsSupplierPortal.Application.Comparison;

/// <summary>FEAT-12.1/FR-CMP-001: one RFQ line item, surfaced as a column/row definition even before
/// any pricing is visible (Group 1's header shape does not depend on the two-envelope gate having
/// opened yet).</summary>
public sealed record ComparisonRfqItemDto(Guid Id, int LineNo, string TitleAr, string TitleEn, decimal Quantity, string UnitOfMeasureCode);

public sealed record ComparisonRequirementAnswerDto(Guid RequirementId, string TextAr, string TextEn, bool IsMandatory, bool Answered);

/// <summary>The two-envelope FINANCIAL content (OQ-009) - only ever populated on
/// ComparisonProposalDto for a proposal that has passed technical qualification in a Consolidated+
/// evaluation; see GetComparisonHandler's own doc comment for the actual gate.</summary>
public sealed record ComparisonItemPriceDto(Guid RfqItemId, decimal Quantity, decimal UnitPrice, decimal? Discount, decimal LineTotal);

/// <summary>FEAT-12.2/FR-CMP-002: one criterion's consolidated (averaged-across-evaluators) score for
/// one proposal - re-derived on read from EvaluatorScore, never persisted separately. Only ever
/// populated once the evaluation is Consolidated+ (BRULE-058: peer scores unreadable before then -
/// re-derivation here would be exactly the same blindness violation as reading the rows directly).</summary>
public sealed record ComparisonCriterionScoreDto(
    Guid CriterionId, string NameAr, string NameEn, bool IsFinancial, decimal Weight, decimal MaxScore,
    decimal? Threshold, decimal AverageScore, bool? MetThreshold);

/// <summary>One Submitted proposal's row in the matrix. Items/GrandTotal/CriterionScores/the
/// evaluation-outcome fields are all nullable for exactly the same reason: absence, not a hidden or
/// masked value, is how this codebase already represents "no read path exists yet" (EPIC-09's own
/// ProposalItem separation, EPIC-11's own EvaluatorScore row-scoping) - never a zero, an empty array
/// standing in for "not visible", or a placeholder string.</summary>
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
    IReadOnlyList<ComparisonCriterionScoreDto>? CriterionScores);

/// <summary>FEAT-12.4/FR-CMP-004: EvaluationState is always the real underlying state (including
/// "NotStarted" spelled out as a literal string when no Evaluation aggregate exists yet at all, not
/// omitted) - the frontend needs it to render the right placeholder ("no proposals yet" vs "awaiting
/// consolidation" vs the real scored matrix), and it carries no evaluation-derived content by
/// itself, so exposing it is not a blindness violation.</summary>
public sealed record ComparisonDto(
    string RfqReferenceCode, string RfqTitleAr, string RfqTitleEn, string EvaluationState,
    IReadOnlyList<ComparisonRfqItemDto> RfqItems,
    IReadOnlyList<ComparisonProposalDto> Proposals);

public interface IGetComparisonHandler
{
    Task<ComparisonDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}
