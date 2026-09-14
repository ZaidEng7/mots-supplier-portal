// Where an evaluation is in its life.
//
//   NotStarted          the tender has closed and nothing has been set up yet
//   Assigned            a committee exists
//   InProgress          at least one evaluator has begun scoring
//   EvaluatorSubmitted  the evaluators are done
//   Consolidated        the scores have been gathered and ranked
//   Finalized           the result is settled
//
// A consolidated evaluation may be reopened back to in progress, with a mandatory reason.

namespace MotsSupplierPortal.Domain.Evaluation;

public enum EvaluationState
{
    NotStarted,
    Assigned,
    InProgress,
    EvaluatorSubmitted,
    Consolidated,
    Finalized,
}
