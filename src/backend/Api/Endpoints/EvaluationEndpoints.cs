using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Evaluations;
using MotsSupplierPortal.Application.Proposals;
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

// T-068: the bid is named by its public code. CriterionId stays a GUID - criteria are snapshot rows
// on the evaluation with no public code of their own, and minting one is T-055's kind of work.
public sealed record ScoreCriterionRequest(string ProposalCode, Guid CriterionId, decimal RawScore, string? CommentAr, string? CommentEn);

public sealed class ScoreCriterionRequestValidator : AbstractValidator<ScoreCriterionRequest>
{
    public ScoreCriterionRequestValidator()
    {
        RuleFor(x => x.RawScore).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ProposalCode).NotEmpty();
    }
}

/// <summary>FEAT-11.2..11.8/FR-EVL-001..011. Buyer/manager-side routes live under
/// /api/v1/rfqs/{referenceCode}/evaluation (mirrors RfqEndpoints' own nesting); evaluator-side
/// scoring routes live under /api/v1/rfqs/{referenceCode}/my-evaluation and are scoped to the
/// caller's own active EvaluationAssignment, never to Organization (see EvaluationLoader's own doc
/// comment).</summary>
public sealed record DeclareConflictRequest(bool HasConflict, string? Reason);

public sealed class DeclareConflictRequestValidator : AbstractValidator<DeclareConflictRequest>
{
    public DeclareConflictRequestValidator() =>
        // A-8: a reason only when there IS a conflict, and then mandatory - an unexplained withdrawal
        // from a committee is not an audit record. A "no conflict" declaration needs no prose.
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000).When(x => x.HasConflict);
}

public sealed record ResolveTieRequest(string ProposalCode, string Reason);

public sealed class ResolveTieRequestValidator : AbstractValidator<ResolveTieRequest>
{
    public ResolveTieRequestValidator()
    {
        RuleFor(x => x.ProposalCode).NotEmpty();
        // A tie broken with no stated basis is what A-1 refuses to let the SYSTEM do, so it must not be
        // what a person does either.
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

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

        // T-028, buyer half. Under the evaluation group because the EVALUATION's state is what
        // gates it (D-7) - putting it under /rfqs/{code}/proposals would have implied a proposal
        // read that does not exist and a gate keyed on the wrong aggregate.
        //
        // Keyed by proposal GUID because that is the identifier a buyer actually holds:
        // ConsolidatedResultDto.ProposalId and MyEvaluationDto.ProposalIds both already emit it.
        // That pre-existing GUID exposure diverges from §3 ("internal identifiers are never exposed
        // in URLs, payloads, or errors") and is recorded rather than widened here - inventing a
        // second addressing scheme for one route would have made the divergence harder to fix, not
        // easier.
        group.MapGet("/proposals/{proposalId:guid}/documents", async (
            string referenceCode, Guid proposalId,
            IGetProposalDocumentsForBuyerHandler handler, CancellationToken ct) =>
        {
            var documents = await handler.HandleAsync(referenceCode, proposalId, ct);
            return documents is null ? Results.NotFound() : Results.Ok(documents);
        })
        .RequirePermission(Permissions.ComparisonView)
        .WithName("GetProposalDocumentsForBuyer");

        group.MapGet("/proposals/{proposalId:guid}/documents/{documentId:guid}/download-url", async (
            string referenceCode, Guid proposalId, Guid documentId,
            IGetProposalDocumentDownloadUrlForBuyerHandler handler, CancellationToken ct) =>
            await handler.HandleAsync(referenceCode, proposalId, documentId, ct) switch
            {
                ProposalDocumentDownloadResult.Success s => Results.Ok(new { url = s.Url, fileName = s.FileName }),
                _ => Results.NotFound(),
            })
        .RequirePermission(Permissions.ComparisonView)
        .WithName("GetProposalDocumentDownloadUrlForBuyer");

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

        // A-1/BRULE-069: a person breaks a tie the rules could not. Same permission as consolidating,
        // because it is the same act - producing the order - and a separate permission would be one
        // more grant to make on every deployment for no additional separation of duty.
        group.MapPost("/resolve-tie", async (
            string referenceCode,
            ResolveTieRequest request,
            IValidator<ResolveTieRequest> validator,
            IResolveEvaluationTieHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(
                new ResolveEvaluationTieCommand(referenceCode, request.ProposalCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.EvaluationConsolidate)
        .WithName("ResolveEvaluationTie");

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

        // A-8/BRULE-067: the recusal declaration window. A GET that deliberately does NOT open scoring -
        // GetMyEvaluation does, as a documented side effect, so an evaluator who loaded the workspace
        // first would have passed this window before ever seeing a bidder's name.
        myGroup.MapGet("/bidders", async (string referenceCode, IGetConflictDeclarationHandler handler, CancellationToken ct) =>
        {
            var declaration = await handler.HandleAsync(referenceCode, ct);
            // §9.2: not assigned is a 404, never a 403 - the same shape every other evaluator-scoped
            // read uses.
            return declaration is null ? Results.NotFound() : Results.Ok(declaration);
        })
        .RequirePermission(Permissions.EvaluationScore)
        .WithName("GetConflictDeclaration");

        myGroup.MapPost("/declare", async (
            string referenceCode, DeclareConflictRequest request, IValidator<DeclareConflictRequest> validator,
            IDeclareConflictHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(
                new DeclareConflictCommand(referenceCode, request.HasConflict, request.Reason), ct));
        })
        .RequirePermission(Permissions.EvaluationScore)
        .WithName("DeclareConflict");

        myGroup.MapPost("/scores", async (
            string referenceCode, ScoreCriterionRequest request, IValidator<ScoreCriterionRequest> validator,
            IScoreCriterionHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMy(await handler.HandleAsync(new ScoreCriterionCommand(
                referenceCode, request.ProposalCode, request.CriterionId, request.RawScore, request.CommentAr, request.CommentEn), ct));
        })
        .RequirePermission(Permissions.EvaluationScore)
        .WithName("ScoreCriterion");

        // T-067: opening a technical supporting file on a bid under evaluation. Under the
        // my-evaluation group because the ASSIGNMENT is the scope, and keyed by the proposal's public
        // code - the same code the workspace read emits, so nothing has to translate an id.
        myGroup.MapGet("/proposals/{proposalCode}/documents/{documentId:guid}/download-url", async (
            string referenceCode, string proposalCode, Guid documentId,
            IGetProposalDocumentDownloadUrlForEvaluatorHandler handler, CancellationToken ct) =>
            await handler.HandleAsync(referenceCode, proposalCode, documentId, ct) switch
            {
                ProposalDocumentDownloadResult.Success s => Results.Ok(new { url = s.Url, fileName = s.FileName }),
                _ => Results.NotFound(),
            })
        .RequirePermission(Permissions.EvaluationScore)
        .WithName("GetProposalDocumentDownloadUrlForEvaluator");

        myGroup.MapPost("/submit", async (string referenceCode, ISubmitEvaluatorHandler handler, CancellationToken ct) =>
            MapMy(await handler.HandleAsync(new SubmitEvaluatorCommand(referenceCode), ct)))
        .RequirePermission(Permissions.EvaluationSubmit)
        .WithName("SubmitEvaluatorScores");
    }
}
