// What an evaluation write can answer, in two separate types.
//
// The buyer's outcomes and the evaluator's own outcomes deliberately do not share a type.
//
// An evaluator who is not assigned to an evaluation must get the same answer as one that does not exist. A
// shared type would eventually grow an outcome that distinguishes the two, and that outcome would tell an
// unassigned evaluator that the evaluation is there.

namespace MotsSupplierPortal.Application.Evaluation;

using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;

public abstract record EvaluationMutationResult
{
    public sealed record Success(EvaluationDto Evaluation) : EvaluationMutationResult;
    public sealed record NotFoundOrOutOfScope : EvaluationMutationResult;
    public sealed record InvalidState(string Message) : EvaluationMutationResult;
}

public abstract record MyEvaluationResult
{
    public sealed record Success(MyEvaluationDto Evaluation) : MyEvaluationResult;
    public sealed record NotFoundOrNotAssigned : MyEvaluationResult;
    public sealed record InvalidState(string Message) : MyEvaluationResult;
}
