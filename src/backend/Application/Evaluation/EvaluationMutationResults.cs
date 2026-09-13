using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;

namespace MotsSupplierPortal.Application.Evaluation;

public abstract record EvaluationMutationResult
{
    public sealed record Success(EvaluationDto Evaluation) : EvaluationMutationResult;
    public sealed record NotFoundOrOutOfScope : EvaluationMutationResult;
    public sealed record InvalidState(string Message) : EvaluationMutationResult;
}

/// <summary>Deliberately not reusing EvaluationMutationResult - see SupplierRfqResult's own doc
/// comment on why a self-service result never shares a type with the buyer-side one: an evaluator
/// who is not assigned to this evaluation must get the same 404 shape as one that does not exist,
/// never a shape that could leak whether it exists.</summary>
public abstract record MyEvaluationResult
{
    public sealed record Success(MyEvaluationDto Evaluation) : MyEvaluationResult;
    public sealed record NotFoundOrNotAssigned : MyEvaluationResult;
    public sealed record InvalidState(string Message) : MyEvaluationResult;
}
