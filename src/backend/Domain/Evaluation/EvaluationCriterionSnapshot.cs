namespace MotsSupplierPortal.Domain.Evaluation;

/// <summary>FR-EVL-002/BUSINESS-PROCESSES.md §5.1: "Instantiate criteria from EvaluationTemplate;
/// snapshot weights" - a frozen copy of the RFQ's already-snapshotted EvaluationTemplateSnapshotJson
/// (RFQ itself snapshots the template at bind time, BindEvaluationTemplateHandler's own doc
/// comment; this is that same snapshot data materialized as real rows on the Evaluation
/// aggregate, not re-read live from EvaluationTemplate a second time).
///
/// <para><b>The two-envelope gate (OQ-009), read directly off Dimension:</b> Dimension ==
/// Commercial marks a criterion as the FINANCIAL envelope; every other Dimension (Technical,
/// Compliance, Delivery) is the TECHNICAL envelope. See Evaluation.ScoreCriterion's own doc
/// comment for the actual gate enforcement.</para></summary>
public sealed class EvaluationCriterionSnapshot
{
    public Guid Id { get; init; }
    public Guid EvaluationId { get; init; }
    public string NameAr { get; init; } = null!;
    public string NameEn { get; init; } = null!;
    public CriterionDimension Dimension { get; init; }
    public decimal Weight { get; init; }
    public decimal MaxScore { get; init; }
    public decimal? Threshold { get; init; }
    public ScoringType ScoringType { get; init; }

    /// <summary>T-021/BRULE-061, snapshotted with the rest. A criterion that required a
    /// justification when the RFQ bound this template still requires one afterwards, even if the
    /// template is later edited - the same reason weights are frozen here.</summary>
    public bool RequiresJustification { get; init; }

    /// <summary>
    /// SCR-501: how to score this criterion, as the template author wrote it.
    ///
    /// <para><b>It was dropped on the way in.</b> `Criterion` has carried guidance since EPIC-07 and this
    /// snapshot did not copy it, so the text existed on the template and reached no evaluator - which is
    /// why `MyEvaluationPage` rendered name, weight and max and nothing else, and why SCR-501 had no
    /// content to render. T-102 recorded the screen as unresolved; the missing half was here.</para>
    ///
    /// <para>Snapshotted rather than read live from the template, for the same reason the weights are: an
    /// evaluator scoring a tender must see the instruction that was in force when the RFQ bound the
    /// template, not one an administrator reworded afterwards.</para>
    /// </summary>
    public string? GuidanceAr { get; init; }

    /// <inheritdoc cref="GuidanceAr"/>
    public string? GuidanceEn { get; init; }

    public bool IsFinancial => Dimension == CriterionDimension.Commercial;
}
