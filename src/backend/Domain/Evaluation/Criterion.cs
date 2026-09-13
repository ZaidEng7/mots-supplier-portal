// One weighted scoring dimension inside an evaluation template.
//
// It is only ever changed through the template's own methods, never constructed or edited directly by
// outside code. That is the same convention every child of a supplier follows.
//
// RequiresJustification means a score against this criterion cannot be submitted without a comment.
// The written rule leaves it open which criteria need one, so the flag sits on the criterion and the
// template's author decides, which is where the rule itself points. Nothing here decides that, say,
// every commercial criterion needs a justification: that would invent the policy the rule declines to
// state, and it would be invisible to the person who wrote the template.
//
// It defaults to off, because a template written before the field existed did not ask for
// justifications, and switching them on retroactively would refuse scores that were legitimate when
// the template was approved.

namespace MotsSupplierPortal.Domain.Evaluation;

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
    public bool RequiresJustification { get; set; }

    public int SortOrder { get; set; }
}
