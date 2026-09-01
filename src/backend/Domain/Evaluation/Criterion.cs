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
    public int SortOrder { get; set; }
}
