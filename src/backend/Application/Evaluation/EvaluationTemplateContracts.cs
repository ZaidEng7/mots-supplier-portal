using MotsSupplierPortal.Domain.Evaluation;

namespace MotsSupplierPortal.Application.Evaluation;

public sealed record CriterionDto(
    Guid Id, string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight, decimal MaxScore,
    decimal? Threshold, ScoringType ScoringType, string? GuidanceAr, string? GuidanceEn, int SortOrder);

public sealed record EvaluationTemplateDto(
    Guid Id, Guid FamilyId, int Version, string NameAr, string NameEn, EvaluationTemplateStatus Status,
    bool IsReferenced, IReadOnlyList<CriterionDto> Criteria,
    // §8.1: the version this read saw, emitted as the ETag and sent back as If-Match.
    uint RowVersion);

public sealed record CreateEvaluationTemplateCommand(string NameAr, string NameEn);

public sealed record AddCriterionCommand(
    Guid TemplateId, string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight, decimal MaxScore,
    decimal? Threshold, ScoringType ScoringType, string? GuidanceAr, string? GuidanceEn);

public sealed record UpdateCriterionCommand(
    Guid TemplateId, Guid CriterionId, string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight,
    decimal MaxScore, decimal? Threshold, ScoringType ScoringType, string? GuidanceAr, string? GuidanceEn);

public sealed record RemoveCriterionCommand(Guid TemplateId, Guid CriterionId);

public abstract record EvaluationTemplateMutationResult
{
    public sealed record Success(EvaluationTemplateDto Template) : EvaluationTemplateMutationResult;
    public sealed record NotFound : EvaluationTemplateMutationResult;
    /// <summary>Wraps every EvaluationTemplate domain-invariant refusal (weight-sum-must-be-100,
    /// immutable-once-referenced, threshold&gt;maxScore, etc.) with the exact DomainException
    /// message, rather than one HTTP error code per invariant - the message is precise enough to
    /// show the caller directly (same pattern as ProfileMutationResult.InvalidState).</summary>
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

/// <summary>The only way to edit a template once it's IsReferenced (EvaluationTemplate.cs's own
/// doc comment) - creates and persists a brand new version row, leaving the referenced row
/// untouched.</summary>
public interface IForkEvaluationTemplateHandler
{
    Task<EvaluationTemplateMutationResult> HandleAsync(Guid id, CancellationToken ct);
}
