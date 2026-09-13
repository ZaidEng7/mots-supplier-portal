using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;

namespace MotsSupplierPortal.Application.Evaluation;

public interface IListEvaluatorCandidatesHandler
{
    /// <summary>Null when the RFQ is not visible to the caller - §9.2's 404, never a 403.</summary>
    Task<IReadOnlyList<EvaluatorCandidateDto>?> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IResolveEvaluationTieHandler
{
    Task<EvaluationMutationResult> HandleAsync(ResolveEvaluationTieCommand command, CancellationToken ct);
}

public interface IOpenEvaluationHandler
{
    Task<EvaluationMutationResult> HandleAsync(OpenEvaluationCommand command, CancellationToken ct);
}

public interface IGetEvaluationHandler
{
    Task<EvaluationDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IAssignEvaluatorsHandler
{
    Task<EvaluationMutationResult> HandleAsync(AssignEvaluatorsCommand command, CancellationToken ct);
}

public interface IRecuseEvaluatorHandler
{
    Task<EvaluationMutationResult> HandleAsync(RecuseEvaluatorCommand command, CancellationToken ct);
}

public interface IConsolidateEvaluationHandler
{
    Task<EvaluationMutationResult> HandleAsync(ConsolidateEvaluationCommand command, CancellationToken ct);
}

public interface IFinalizeEvaluationHandler
{
    Task<EvaluationMutationResult> HandleAsync(FinalizeEvaluationCommand command, CancellationToken ct);
}

public interface IReopenEvaluationHandler
{
    Task<EvaluationMutationResult> HandleAsync(ReopenEvaluationCommand command, CancellationToken ct);
}

public interface IGetMyEvaluationHandler
{
    Task<MyEvaluationResult> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IScoreCriterionHandler
{
    Task<MyEvaluationResult> HandleAsync(ScoreCriterionCommand command, CancellationToken ct);
}

public interface ISubmitEvaluatorHandler
{
    Task<MyEvaluationResult> HandleAsync(SubmitEvaluatorCommand command, CancellationToken ct);
}

public interface IGetConflictDeclarationHandler
{
    Task<ConflictDeclarationDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct);
}

public interface IDeclareConflictHandler
{
    Task<EvaluationMutationResult> HandleAsync(DeclareConflictCommand command, CancellationToken ct);
}
