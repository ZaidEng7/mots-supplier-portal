using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record CreateEvaluationTemplateRequest(string NameAr, string NameEn);

public sealed class CreateEvaluationTemplateRequestValidator : AbstractValidator<CreateEvaluationTemplateRequest>
{
    public CreateEvaluationTemplateRequestValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
    }
}

public sealed record CriterionRequest(
    string NameAr, string NameEn, CriterionDimension Dimension, decimal Weight, decimal MaxScore,
    decimal? Threshold, ScoringType ScoringType, string? GuidanceAr, string? GuidanceEn);

public sealed class CriterionRequestValidator : AbstractValidator<CriterionRequest>
{
    public CriterionRequestValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Weight).GreaterThan(0).LessThanOrEqualTo(100);
        RuleFor(x => x.MaxScore).GreaterThan(0);
        RuleFor(x => x.Threshold).LessThanOrEqualTo(x => x.MaxScore).When(x => x.Threshold is not null);
    }
}

/// <summary>FEAT-11.1/FR-ADM-005, pulled forward for EPIC-07 - EPIC-07's evaluation-template
/// binding needs a real, Active template to bind to.</summary>
public static class EvaluationTemplateEndpoints
{
    private static IResult MapMutation(EvaluationTemplateMutationResult result) => result switch
    {
        EvaluationTemplateMutationResult.Success s => Results.Ok(s.Template),
        EvaluationTemplateMutationResult.NotFound => Results.NotFound(),
        EvaluationTemplateMutationResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        _ => Results.Problem(),
    };

    public static void MapEvaluationTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/evaluation-templates").WithTags("EvaluationTemplates")
            .RequirePermission(Permissions.EvaluationTemplateManage);

        group.MapGet("/", async (IListEvaluationTemplatesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("ListEvaluationTemplates");

        group.MapGet("/{id:guid}", async (Guid id, IGetEvaluationTemplateHandler handler, CancellationToken ct) =>
        {
            var template = await handler.HandleAsync(id, ct);
            return template is null ? Results.NotFound() : Results.Ok(template);
        })
        .WithETag()
        .WithName("GetEvaluationTemplate");

        group.MapPost("/", async (
            CreateEvaluationTemplateRequest request,
            IValidator<CreateEvaluationTemplateRequest> validator,
            ICreateEvaluationTemplateHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(new CreateEvaluationTemplateCommand(request.NameAr, request.NameEn), ct));
        })
        .WithName("CreateEvaluationTemplate");

        group.MapPost("/{id:guid}/criteria", async (
            Guid id,
            CriterionRequest request,
            IValidator<CriterionRequest> validator,
            IManageCriterionHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.AddAsync(new AddCriterionCommand(
                id, request.NameAr, request.NameEn, request.Dimension, request.Weight, request.MaxScore,
                request.Threshold, request.ScoringType, request.GuidanceAr, request.GuidanceEn), ct);
            return MapMutation(result);
        })
        .WithName("AddCriterion");

        group.MapPut("/{id:guid}/criteria/{criterionId:guid}", async (
            Guid id,
            Guid criterionId,
            CriterionRequest request,
            IValidator<CriterionRequest> validator,
            IManageCriterionHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.UpdateAsync(new UpdateCriterionCommand(
                id, criterionId, request.NameAr, request.NameEn, request.Dimension, request.Weight, request.MaxScore,
                request.Threshold, request.ScoringType, request.GuidanceAr, request.GuidanceEn), ct);
            return MapMutation(result);
        })
        .WithName("UpdateCriterion");

        group.MapDelete("/{id:guid}/criteria/{criterionId:guid}", async (
            Guid id, Guid criterionId, IManageCriterionHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveCriterionCommand(id, criterionId), ct)))
        .WithName("RemoveCriterion");

        group.MapPost("/{id:guid}/activate", async (Guid id, IActivateEvaluationTemplateHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(id, ct)))
        .RequireIfMatch()
        .WithName("ActivateEvaluationTemplate");

        group.MapPost("/{id:guid}/archive", async (Guid id, IArchiveEvaluationTemplateHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(id, ct)))
        .RequireIfMatch()
        .WithName("ArchiveEvaluationTemplate");

        group.MapPost("/{id:guid}/fork", async (Guid id, IForkEvaluationTemplateHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(id, ct)))
        .WithName("ForkEvaluationTemplate");
    }
}
