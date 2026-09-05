namespace MotsSupplierPortal.Domain.Evaluation;

/// <summary>FEAT-11.6/FR-EVL-007, BRULE-063/064: one row per proposal, computed at Consolidate().
/// TechnicallyQualified is the two-envelope gate's own consolidated confirmation (OQ-009) - a
/// proposal that fails it is excluded from ranking regardless of any financial score it may have
/// accumulated (BRULE-064: "not shortlist-eligible ... regardless of total").</summary>
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

    /// <summary>
    /// A-1/BRULE-069: this result is tied with another one after EVERY tie-break rung, and the tie has
    /// not been resolved by a person.
    ///
    /// <para>The ranks are still assigned - a list with no order is useless - but the award flow
    /// refuses to offer rank 1 while any rank-1 result carries this, because at that point the
    /// ordering between them came from nothing a rule decided. D-8's principle: deterministic where
    /// the rules decide, refusing to decide where they do not.</para>
    /// </summary>
    public bool TieUnresolved { get; internal set; }

    /// <summary>Who resolved the tie and why, once a person has. Null while unresolved and null when
    /// there was never a tie to resolve.</summary>
    public Guid? TieResolvedByUserId { get; internal set; }

    public string? TieResolutionReason { get; internal set; }
}
