// The scoring templates: listing them, reading one, creating and editing criteria, activating, archiving and
// forking.
//
// Whether a criterion requires a written justification is the template author's decision, because the rule
// declines to say which criteria need one. Omitting it means no, which is what a template written before the
// field existed asked for.
//
//
// WHY THE PERMISSION IS ON EACH ROUTE AND NOT ON THE GROUP
//
// It used to be on the group, and a group-level check applies to every route including the reads, so widening
// one route could not undo it. The officer stayed forbidden no matter what the list declared.
//
// The reads have to be wider than the writes. Anybody who may bind a template to a tender needs to see the
// list, and binding is gated on editing a tender, which a procurement officer holds while the
// template-management permission is a manager's. So the officer could bind a template they were forbidden to
// list: the picker came back empty, and a tender could not reach internal review at all, because binding one
// is a precondition of submitting it. That is the same family of defect as a step built for a persona the
// permissions would not let complete it.
//
// The writes still require the management permission, individually and visibly. Only the reads are wider.
//
// The single-item read needs its permission stated for a second reason: moving the check off the group left
// that route with nothing but a requirement to be signed in, which would have let any account, a supplier
// included, read a buying body's scoring scheme. Caught by the generated permission catalogue, which noticed
// the route had dropped out of the table entirely.
//
//
// THE MISSING VERSION HEADER
//
// Every route here returns a shape whose version field is documented as the one a caller sends back as a
// write precondition, and nothing emitted it. Meanwhile activating and archiving both demand that
// precondition, and the list carried no version either, so a client could never obtain what those two
// required.
//
// The effect was that activating a template through the interface was impossible: it was refused every time.
// Found by walking a tender through from an empty database, where a template has to be created and activated
// before any tender can go to internal review.
//
// Forking does not require a precondition, because it reads an existing template and writes a new one, so
// there is no version of the fork to assert.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;

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
    decimal? Threshold, ScoringType ScoringType, string? GuidanceAr, string? GuidanceEn,
    bool RequiresJustification = false);

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
            .RequireAuthorization();

        group.MapGet("/", async (IListEvaluationTemplatesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequireAnyPermission(Permissions.EvaluationTemplateManage, Permissions.RfqEdit)
        .WithName("ListEvaluationTemplates");

        group.MapGet("/{id:guid}", async (Guid id, IGetEvaluationTemplateHandler handler, CancellationToken ct) =>
        {
            var template = await handler.HandleAsync(id, ct);
            return template is null ? Results.NotFound() : Results.Ok(template);
        })
        .WithETag()
        .RequireAnyPermission(Permissions.EvaluationTemplateManage, Permissions.RfqEdit)
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
        .RequirePermission(Permissions.EvaluationTemplateManage)
        .WithFreshETag()
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
                request.Threshold, request.ScoringType, request.GuidanceAr, request.GuidanceEn,
                request.RequiresJustification), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.EvaluationTemplateManage)
        .WithFreshETag()
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
                request.Threshold, request.ScoringType, request.GuidanceAr, request.GuidanceEn,
                request.RequiresJustification), ct);
            return MapMutation(result);
        })
        .RequirePermission(Permissions.EvaluationTemplateManage)
        .WithFreshETag()
        .WithName("UpdateCriterion");

        group.MapDelete("/{id:guid}/criteria/{criterionId:guid}", async (
            Guid id, Guid criterionId, IManageCriterionHandler handler, CancellationToken ct) =>
            MapMutation(await handler.RemoveAsync(new RemoveCriterionCommand(id, criterionId), ct)))
        .RequirePermission(Permissions.EvaluationTemplateManage)
        .WithFreshETag()
        .WithName("RemoveCriterion");

        group.MapPost("/{id:guid}/activate", async (Guid id, IActivateEvaluationTemplateHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(id, ct)))
        .RequireIfMatch()
        .RequirePermission(Permissions.EvaluationTemplateManage)
        .WithFreshETag()
        .WithName("ActivateEvaluationTemplate");

        group.MapPost("/{id:guid}/archive", async (Guid id, IArchiveEvaluationTemplateHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(id, ct)))
        .RequireIfMatch()
        .RequirePermission(Permissions.EvaluationTemplateManage)
        .WithFreshETag()
        .WithName("ArchiveEvaluationTemplate");

        group.MapPost("/{id:guid}/fork", async (Guid id, IForkEvaluationTemplateHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(id, ct)))
        .RequirePermission(Permissions.EvaluationTemplateManage)
        .WithFreshETag()
        .WithName("ForkEvaluationTemplate");
    }
}
