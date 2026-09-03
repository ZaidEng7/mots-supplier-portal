using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Evaluations;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record AssignEvaluatorsRequest(IReadOnlyList<Guid> EvaluatorUserIds);

public sealed class AssignEvaluatorsRequestValidator : AbstractValidator<AssignEvaluatorsRequest>
{
    public AssignEvaluatorsRequestValidator() => RuleFor(x => x.EvaluatorUserIds).NotEmpty();
}

public sealed record RecuseEvaluatorRequest(Guid EvaluatorUserId, string Reason);

public sealed class RecuseEvaluatorRequestValidator : AbstractValidator<RecuseEvaluatorRequest>
{
    public RecuseEvaluatorRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
}

public sealed record ReopenEvaluationRequest(string Reason);

public sealed class ReopenEvaluationRequestValidator : AbstractValidator<ReopenEvaluationRequest>
{
    public ReopenEvaluationRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
}

public sealed record ScoreCriterionRequest(Guid ProposalId, Guid CriterionId, decimal RawScore, string? CommentAr, string? CommentEn);

public sealed class ScoreCriterionRequestValidator : AbstractValidator<ScoreCriterionRequest>
{
    public ScoreCriterionRequestValidator() => RuleFor(x => x.RawScore).GreaterThanOrEqualTo(0);
}

/// <summary>FEAT-11.2..11.8/FR-EVL-001..011. Buyer/manager-side routes live under
/// /api/v1/rfqs/{referenceCode}/evaluation (mirrors RfqEndpoints' own nesting); evaluator-side
/// scoring routes live under /api/v1/rfqs/{referenceCode}/my-evaluation and are scoped to the
/// caller's own active EvaluationAssignment, never to Organization (see EvaluationLoader's own doc
/// comment).</summary>
public static class EvaluationEndpoints
{
    private static IResult MapMutation(EvaluationMutationResult result) => result switch
    {
        EvaluationMutationResult.Success s => Results.Ok(s.Evaluation),
        EvaluationMutationResult.NotFoundOrOutOfScope => Results.NotFound(),
        EvaluationMutationResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        _ => Results.Problem(),
    };

    private static IResult MapMy(MyEvaluationResult result) => result switch
    {
        MyEvaluationResult.Success s => Results.Ok(s.Evaluation),
        MyEvaluationResult.NotFoundOrNotAssigned => Results.NotFound(),
        MyEvaluationResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        _ => Results.Problem(),
    };

    public static void MapEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rfqs/{referenceCode}/evaluation").WithTags("Evaluation");

        group.MapGet("/", async (string referenceCode, IGetEvaluationHandler handler, CancellationToken ct) =>
        {
            var evaluation = await handler.HandleAsync(referenceCode, ct);
            return evaluation is null ? Results.NotFound() : Results.Ok(evaluation);
        })
        .RequirePermission(Permissions.EvaluationOpen)
        .WithETag()
        .WithName("GetEvaluation");

        group.MapPost("/open", async (string referenceCode, IOpenEvaluationHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new OpenEvaluationCommand(referenceCode), ct)))
        .RequirePermission(Permissions.EvaluationOpen)
        .WithName("OpenEvaluation");

        group.MapPost("/assignments", async (
            string referenceCode, AssignEvaluatorsRequest request, IValidator<AssignEvaluatorsRequest> validator,
            IAssignEvaluatorsHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(new AssignEvaluatorsCommand(referenceCode, request.EvaluatorUserIds), ct));
        })
        .RequirePermission(Permissions.EvaluationAssign)
        .WithName("AssignEvaluators");

        group.MapPost("/recuse", async (
            string referenceCode, RecuseEvaluatorRequest request, IValidator<RecuseEvaluatorRequest> validator,
            IRecuseEvaluatorHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(new RecuseEvaluatorCommand(referenceCode, request.EvaluatorUserId, request.Reason), ct));
        })
        .RequirePermission(Permissions.EvaluationAssign)
        .WithName("RecuseEvaluator");

        group.MapPost("/consolidate", async (string referenceCode, IConsolidateEvaluationHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new ConsolidateEvaluationCommand(referenceCode), ct)))
        .RequirePermission(Permissions.EvaluationConsolidate)
        .RequireIfMatch()
        .WithName("ConsolidateEvaluation");

        group.MapPost("/finalize", async (string referenceCode, IFinalizeEvaluationHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new FinalizeEvaluationCommand(referenceCode), ct)))
        .RequirePermission(Permissions.EvaluationFinalize)
        .RequireIfMatch()
        .WithName("FinalizeEvaluation");

        group.MapPost("/reopen", async (
            string referenceCode, ReopenEvaluationRequest request, IValidator<ReopenEvaluationRequest> validator,
            IReopenEvaluationHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(new ReopenEvaluationCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.EvaluationReopen)
        .RequireIfMatch()
        .WithName("ReopenEvaluation");

        // SCR-500 / FR-DSH-004 / T3-02. The evaluator's own assignments, across RFQs.
        //
        // A COLLECTION of its own rather than a sub-resource of one RFQ, because that is what it is:
        // "the evaluations assigned to me" has no single parent RFQ, and hanging it off one would
        // mean the caller already knowing which RFQ to ask about - which is the thing this screen
        // exists to tell them. EPIC-11 was complete and unreachable for exactly that reason.
        app.MapGet("/api/v1/my-evaluations", async (
            string? tab,
            IListMyAssignmentsHandler handler,
            CancellationToken ct) =>
        {
            // An unrecognised tab must not be dropped: dropping the filter returns everything, so a
            // caller that asked to narrow gets the opposite with no way to tell. Same answer as
            // Batch 0.2's unknown filter values.
            if (tab is not null && !MyAssignmentTabs.All.Contains(tab))
            {
                return FilterValues.InvalidFilterValue("tab", tab);
            }

            return Results.Ok(await handler.HandleAsync(tab, ct));
        })
        .RequirePermission(Permissions.EvaluationScore)
        .WithTags("Evaluation")
        .WithName("ListMyAssignments");

        var myGroup = app.MapGroup("/api/v1/rfqs/{referenceCode}/my-evaluation").WithTags("Evaluation");

        myGroup.MapGet("/", async (string referenceCode, IGetMyEvaluationHandler handler, CancellationToken ct) =>
            MapMy(await handler.HandleAsync(referenceCode, ct)))
        .RequirePermission(Permissions.EvaluationScore)
        .WithName("GetMyEvaluation");

        myGroup.MapPost("/scores", async (
            string referenceCode, ScoreCriterionRequest request, IValidator<ScoreCriterionRequest> validator,
            IScoreCriterionHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMy(await handler.HandleAsync(new ScoreCriterionCommand(
                referenceCode, request.ProposalId, request.CriterionId, request.RawScore, request.CommentAr, request.CommentEn), ct));
        })
        .RequirePermission(Permissions.EvaluationScore)
        .WithName("ScoreCriterion");

        myGroup.MapPost("/submit", async (string referenceCode, ISubmitEvaluatorHandler handler, CancellationToken ct) =>
            MapMy(await handler.HandleAsync(new SubmitEvaluatorCommand(referenceCode), ct)))
        .RequirePermission(Permissions.EvaluationSubmit)
        .WithName("SubmitEvaluatorScores");
    }
}
