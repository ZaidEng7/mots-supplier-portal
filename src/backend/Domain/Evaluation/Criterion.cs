namespace MotsSupplierPortal.Domain.Evaluation;

/// <summary>One weighted scoring dimension within an EvaluationTemplate (DOMAIN-MODEL.md §5.6).
/// Mutated only via EvaluationTemplate's own methods (AddCriterion/UpdateCriterion/RemoveCriterion) -
/// never constructed or edited directly by external code, matching Supplier's own child-entity
/// convention (e.g. Representative/Address).</summary>
public sealed class Criterion
{
    public Guid Id { get; init; }
    public Guid EvaluationTemplateId { get; init; }
    public string NameAr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public CriterionDimension Dimension { get; set; }
    public decimal Weight { get; set; }
    public decimal MaxScore { get; set; }
    public decimal? Threshold { get; set; }
    public ScoringType ScoringType { get; set; }
    public string? GuidanceAr { get; set; }
    public string? GuidanceEn { get; set; }
    /// <summary>
    /// T-021/BRULE-061: "Criteria requiring justification cannot be submitted without a comment."
    ///
    /// <para>The rule tags WHICH criteria require one as <c>[ASSUMPTION]</c>, so the flag sits on
    /// the criterion and the template author sets it - which is where the document points. Nothing
    /// here decides that, say, every Commercial criterion needs a justification: that would be
    /// inventing the policy the rule declines to state, and it would be invisible to the person who
    /// authored the template.</para>
    ///
    /// <para>Defaults to false. A template written before this field existed did not ask for
    /// justifications, and turning them on retroactively would refuse scores that were legitimate
    /// when the template was approved.</para>
    /// </summary>
    public bool RequiresJustification { get; set; }

    public int SortOrder { get; set; }
}
