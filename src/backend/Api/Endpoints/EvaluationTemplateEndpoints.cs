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
    decimal? Threshold, ScoringType ScoringType, string? GuidanceAr, string? GuidanceEn,
    // T-021/BRULE-061: the template author decides, because the rule declines to say which criteria
    // need one. Omitted means false, which is what a template written before the field asked for.
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
        // The permission moved OFF the group in batch 12 and onto each route.
        //
        // A group filter applies to every route including the GET, so a route-level widening cannot
        // undo it - the officer stayed forbidden no matter what the list declared. The writes still
        // require evaluation.template.manage, individually and visibly; only the read is wider.
        var group = app.MapGroup("/api/v1/evaluation-templates").WithTags("EvaluationTemplates")
            .RequireAuthorization();

        group.MapGet("/", async (IListEvaluationTemplatesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        // Readable by whoever may BIND a template as well as by whoever maintains them. Binding is
        // gated on rfq.edit (RfqEndpoints, BindEvaluationTemplate) and a procurement officer holds
        // that but not evaluation.template.manage - so the officer could bind a template they were
        // forbidden to list, the picker came back empty, and a tender could not reach internal review
        // because binding one is a precondition of submitting it. Same family as D-43 and D-44: a
        // step built for a persona the permissions would not let complete it.
        .RequireAnyPermission(Permissions.EvaluationTemplateManage, Permissions.RfqEdit)
        .WithName("ListEvaluationTemplates");

        group.MapGet("/{id:guid}", async (Guid id, IGetEvaluationTemplateHandler handler, CancellationToken ct) =>
        {
            var template = await handler.HandleAsync(id, ct);
            return template is null ? Results.NotFound() : Results.Ok(template);
        })
        .WithETag()
        // Same audience as the list, and it must be stated HERE: moving the permission off the group
        // left this route with nothing but RequireAuthorization, which would have let any signed-in
        // account - a supplier included - read a buying body's scoring scheme. Caught by the generated
        // permission catalogue, which noticed the route had dropped out of the table entirely.
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
        // Added in batch 12. Every route below returns EvaluationTemplateDto, whose RowVersion carries the
        // comment "the version this read saw, emitted as the ETag and sent back as If-Match" - and nothing
        // emitted it. Meanwhile activate and archive declare RequireIfMatch, and the list GET carries no
        // ETag either, so a client could never obtain the version those two demand. (Fork does not require
        // one: it reads an existing template and writes a NEW one, so there is no version of the fork to
        // assert. The comment said "activate, archive and fork" until phase 2's sweep enumerated the routes
        // that actually carry the marker.)
        //
        // The effect was that activating a template through the interface was impossible: it answered 428
        // every time. Found by walking a tender from an empty database, where a template has to be created
        // and activated before any RFQ can go to internal review.
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
