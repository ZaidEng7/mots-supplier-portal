// The vocabulary for the scoring templates: what a template and a criterion look like, what can be asked of
// them, and what the answers are.
//
// Every refusal the domain makes, the weights not summing to a hundred, a template already bound to a tender,
// a threshold above the maximum score, arrives as one outcome carrying the domain's own message rather than
// one status code per rule. The message is precise enough to show the caller directly.

namespace MotsSupplierPortal.Application.Evaluation;

using MotsSupplierPortal.Domain.Evaluation;

public sealed record CriterionDto(
    Guid Id, string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight, decimal MaxScore,
    decimal? Threshold, ScoringType ScoringType, string? GuidanceAr, string? GuidanceEn, int SortOrder,
    bool RequiresJustification = false);

public sealed record EvaluationTemplateDto(
    Guid Id, Guid FamilyId, int Version, string NameAr, string NameEn, EvaluationTemplateStatus Status,
    bool IsReferenced, IReadOnlyList<CriterionDto> Criteria,
    uint RowVersion);

public sealed record CreateEvaluationTemplateCommand(string NameAr, string NameEn);

public sealed record AddCriterionCommand(
    Guid TemplateId, string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight, decimal MaxScore,
    decimal? Threshold, ScoringType ScoringType, string? GuidanceAr, string? GuidanceEn,
    bool RequiresJustification = false);

public sealed record UpdateCriterionCommand(
    Guid TemplateId, Guid CriterionId, string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight,
    decimal MaxScore, decimal? Threshold, ScoringType ScoringType, string? GuidanceAr, string? GuidanceEn,
    bool RequiresJustification = false);

public sealed record RemoveCriterionCommand(Guid TemplateId, Guid CriterionId);

public abstract record EvaluationTemplateMutationResult
{
    public sealed record Success(EvaluationTemplateDto Template) : EvaluationTemplateMutationResult;
    public sealed record NotFound : EvaluationTemplateMutationResult;
    public sealed record InvalidState(string Message) : EvaluationTemplateMutationResult;
}

public interface IListEvaluationTemplatesHandler
{
    Task<IReadOnlyList<EvaluationTemplateDto>> HandleAsync(CancellationToken ct);
}

public interface IGetEvaluationTemplateHandler
{
    Task<EvaluationTemplateDto?> HandleAsync(Guid id, CancellationToken ct);
}

public interface ICreateEvaluationTemplateHandler
{
    Task<EvaluationTemplateMutationResult> HandleAsync(CreateEvaluationTemplateCommand command, CancellationToken ct);
}

public interface IManageCriterionHandler
{
    Task<EvaluationTemplateMutationResult> AddAsync(AddCriterionCommand command, CancellationToken ct);
    Task<EvaluationTemplateMutationResult> UpdateAsync(UpdateCriterionCommand command, CancellationToken ct);
    Task<EvaluationTemplateMutationResult> RemoveAsync(RemoveCriterionCommand command, CancellationToken ct);
}

public interface IActivateEvaluationTemplateHandler
{
    Task<EvaluationTemplateMutationResult> HandleAsync(Guid id, CancellationToken ct);
}

public interface IArchiveEvaluationTemplateHandler
{
    Task<EvaluationTemplateMutationResult> HandleAsync(Guid id, CancellationToken ct);
}

public interface IForkEvaluationTemplateHandler
{
    Task<EvaluationTemplateMutationResult> HandleAsync(Guid id, CancellationToken ct);
}
