// One evaluator assigned to one evaluation.
//
// Every evaluator is assigned to every submitted bid on the tender, which is the ordinary
// committee-scoring arrangement. Nothing in the written process says which bids would go to which
// evaluator, so no subsetting scheme was invented. Each score is still stored per evaluator, per bid
// and per criterion individually, so introducing per-evaluator subsets later needs no change to the
// database.
//
// RecusedAt and RecusalReason record an evaluator stepping aside. An assignment is active until then.
//
// ConflictDeclaredAt is when this evaluator saw the bidder list and declared whether they had a
// conflict. Declaring is an assignment-time act, and that is what makes anonymous scoring compatible
// with the rule requiring recusal rather than in conflict with it: the evaluator is shown the bidders
// once, declares, and is then either recused or proceeds, after which the names are withheld until the
// scores are gathered. Nobody has to recuse themselves from a bidder they cannot see, because the
// declaration already happened.
//
// It is stored per assignment rather than per evaluation on purpose. The evaluation moves to in
// progress when the first evaluator opens scoring, so one shared flag would close the second
// evaluator's declaration window before they ever had one.

namespace MotsSupplierPortal.Domain.Evaluation;

public sealed class EvaluationAssignment
{
    public Guid Id { get; init; }
    public Guid EvaluationId { get; init; }
    public Guid EvaluatorUserId { get; init; }
    public DateTimeOffset AssignedAt { get; init; }
    public DateTimeOffset? SubmittedAt { get; internal set; }
    public DateTimeOffset? RecusedAt { get; internal set; }
    public string? RecusalReason { get; internal set; }

    public DateTimeOffset? ConflictDeclaredAt { get; internal set; }

    public bool IsActive => RecusedAt is null;
}
