// The evaluation routes, split between the people who run an evaluation and the people who score it.
//
// The manager's and officer's routes sit under a tender's own evaluation path, mirroring how the tender
// routes nest. The evaluator's scoring routes sit under a separate path and are scoped to the caller's own
// active assignment rather than to an organization.
//
//
// HOW THINGS ARE NAMED ON THE WIRE
//
// A bid is named by its public code. A criterion is still named by its internal identifier, because criteria
// are frozen rows on the evaluation with no public code of their own, and minting one is a piece of work in
// its own right.
//
// One route is keyed by a bid's internal identifier rather than its code: opening a technical file on a bid
// under evaluation from the buyer's side. That is the identifier a buyer actually holds, because two response
// shapes already emit it. That pre-existing exposure diverges from the rule that internal identifiers never
// appear in addresses, and it is recorded rather than widened here. Inventing a second addressing scheme for
// one route would have made the divergence harder to fix rather than easier.
//
// The evaluator's own version of that route is keyed by the bid's public code, which is the code the
// workspace read emits, so nothing has to translate anything.
//
// Both file routes live under the group whose state gates them. The buyer's is under the evaluation rather
// than under the bid, because it is the evaluation's state that decides access; putting it under the bid
// would imply a bid read that does not exist and a gate keyed on the wrong record. The evaluator's is under
// the assignment, because the assignment is the scope.
//
//
// TWO REASONS THAT ARE MANDATORY, AND ONE THAT IS NOT
//
// Declaring a conflict requires a reason; declaring no conflict does not. An unexplained withdrawal from a
// committee is not an audit record, and a declaration that there is nothing to declare needs no prose.
//
// Breaking a tie requires a reason. A tie broken with no stated basis is exactly what the system refuses to
// do on its own, so it must not be what a person does either.
//
//
// PERMISSIONS THAT ARE SHARED ON PURPOSE
//
// The list of people a manager may assign sits behind the same permission as assigning them. The list exists
// only to make that action performable, and a wider gate would be a roster of ministry staff readable by
// anyone who can open a tender.
//
// That list did not exist, and the screen could not work without it: assigning was a free-text box for a raw
// identifier, and the only staff list in the product requires a permission a procurement manager does not
// hold. Found by walking a tender through in the browser.
//
// Breaking a tie sits behind the same permission as consolidating, because it is the same act, producing the
// order, and a separate permission would be one more grant to make on every deployment for no additional
// separation of duty.
//
//
// THE EVALUATOR'S OWN SURFACE
//
// The evaluator's assignments are a collection of their own rather than a sub-resource of one tender, because
// that is what they are. "The evaluations assigned to me" has no single parent tender, and hanging it off one
// would require the caller to already know which tender to ask about, which is the thing this screen exists
// to tell them. The whole evaluation feature was complete and unreachable for exactly that reason.
//
// An unrecognised tab filter is refused rather than dropped, because dropping it returns everything, so a
// caller that asked to narrow gets the opposite with no way to tell.
//
// The conflict-declaration read deliberately does not open scoring. The main workspace read does open it, as
// a documented side effect, so an evaluator who loaded the workspace first would have passed the declaration
// window before ever seeing a bidder's name.
//
// An evaluator who is not assigned gets not-found rather than refused, the same shape every other
// evaluator-scoped read uses.
//
// Every transition puts the new version on its own response, so a second transition has a precondition to
// send without waiting for a re-read.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Identity;

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

public sealed record ScoreCriterionRequest(string ProposalCode, Guid CriterionId, decimal RawScore, string? CommentAr, string? CommentEn);

public sealed class ScoreCriterionRequestValidator : AbstractValidator<ScoreCriterionRequest>
{
    public ScoreCriterionRequestValidator()
    {
        RuleFor(x => x.RawScore).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ProposalCode).NotEmpty();
    }
}

public sealed record DeclareConflictRequest(bool HasConflict, string? Reason);

public sealed class DeclareConflictRequestValidator : AbstractValidator<DeclareConflictRequest>
{
    public DeclareConflictRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000).When(x => x.HasConflict);
}

public sealed record ResolveTieRequest(string ProposalCode, string Reason);

public sealed class ResolveTieRequestValidator : AbstractValidator<ResolveTieRequest>
{
    public ResolveTieRequestValidator()
    {
        RuleFor(x => x.ProposalCode).NotEmpty();
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

        group.MapGet("/candidates", async (
            string referenceCode, IListEvaluatorCandidatesHandler handler, CancellationToken ct) =>
        {
            var candidates = await handler.HandleAsync(referenceCode, ct);
            return candidates is null ? Results.NotFound() : Results.Ok(candidates);
        })
        .RequirePermission(Permissions.EvaluationAssign)
        .WithName("ListEvaluatorCandidates");

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
        .WithFreshETag()
.WithName("ConsolidateEvaluation");

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
        .WithFreshETag()
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
        .WithFreshETag()
.WithName("ReopenEvaluation");

        app.MapGet("/api/v1/my-evaluations", async (
            string? tab,
            IListMyAssignmentsHandler handler,
            CancellationToken ct) =>
        {
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

        myGroup.MapGet("/bidders", async (string referenceCode, IGetConflictDeclarationHandler handler, CancellationToken ct) =>
        {
            var declaration = await handler.HandleAsync(referenceCode, ct);
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
