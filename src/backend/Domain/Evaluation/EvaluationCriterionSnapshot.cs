// One criterion, frozen as it stood when this evaluation began.
//
// The tender already freezes its evaluation template when it binds one. These rows are that same
// frozen data materialised as real rows on the evaluation, rather than the live template read a second
// time.
//
// Dimension is what the two-envelope gate reads. Commercial marks a criterion as the financial
// envelope, and every other dimension is the technical one. IsFinancial says so directly.
//
// RequiresJustification is frozen with the rest. A criterion that required a comment when the tender
// bound this template still requires one afterwards, even if the template is later edited.
//
// GuidanceAr and GuidanceEn are how to score this criterion, as the template's author wrote it. They
// were dropped on the way in for a while: the live criterion had carried guidance for a long time and
// this snapshot did not copy it, so the text existed on the template and reached no evaluator. That is
// why the evaluator's screen showed a name, a weight and a maximum and nothing else, and why the
// screen designed around that guidance had no content to render.
//
// The guidance is frozen rather than read live for the same reason the weights are: an evaluator
// scoring a tender must see the instruction that was in force when the tender bound the template, not
// one an administrator reworded afterwards.

namespace MotsSupplierPortal.Domain.Evaluation;

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

    public bool RequiresJustification { get; init; }

    public string? GuidanceAr { get; init; }

    public string? GuidanceEn { get; init; }

    public bool IsFinancial => Dimension == CriterionDimension.Commercial;
}
