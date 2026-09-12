using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Awards;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// T-068: the winner is named by the bid's public code. <c>WinningProposalId</c> is still accepted so
/// this stays an ADDITIVE change - API-ARCHITECTURE.md's versioning table makes renaming a field a
/// version-bump trigger, and a request that used to work must keep working. The code wins when both
/// arrive; see DECISIONS-TAKEN.md D-69.
/// </summary>
public sealed record RecommendAwardRequest(
    string JustificationAr, string JustificationEn,
    // Defaults, so neither identifier is REQUIRED in the generated schema. The contract gate caught
    // the alternative: a nullable parameter with no default still generates as required, which would
    // have made `winningProposalCode` a new demand on every existing caller - the precise breakage
    // this whole rework exists to avoid.
    string? WinningProposalCode = null, Guid? WinningProposalId = null);

public sealed class RecommendAwardRequestValidator : AbstractValidator<RecommendAwardRequest>
{
    public RecommendAwardRequestValidator()
    {
        RuleFor(x => x.JustificationAr).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.JustificationEn).NotEmpty().MaximumLength(4000);
    }
}

public sealed record RejectAwardRequest(string Reason);

public sealed class RejectAwardRequestValidator : AbstractValidator<RejectAwardRequest>
{
    public RejectAwardRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
}

/// <summary>FEAT-14.1..14.6/FR-AWD-001..007. State-transition endpoints are permission-guarded per
/// BUSINESS-PROCESSES.md §6.1's own actor/permission column (verified directly against that table),
/// same discipline as RfqEndpoints/EvaluationEndpoints.</summary>
public static class AwardEndpoints
{
    private static IResult MapMutation(AwardMutationResult result) => result switch
    {
        AwardMutationResult.Success s => Results.Ok(s.Award),
        AwardMutationResult.NotFoundOrOutOfScope => Results.NotFound(),
        AwardMutationResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        AwardMutationResult.SegregationOfDutiesViolation => Results.BadRequest(new { error = "segregation_of_duties_violation", message = "The approver must differ from the recommender." }),
        AwardMutationResult.SupplierNotActive => Results.BadRequest(new { error = "supplier_not_active" }),
        _ => Results.Problem(),
    };

    public static void MapAwardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rfqs/{referenceCode}/award").WithTags("Award");

        group.MapGet("/", async (string referenceCode, IGetAwardHandler handler, CancellationToken ct) =>
        {
            var award = await handler.HandleAsync(referenceCode, ct);
            return award is null ? Results.NotFound() : Results.Ok(award);
        })
        .RequirePermission(Permissions.AwardRecommend)
        .WithETag()
        .WithName("GetAward");

        group.MapPost("/recommend", async (
            string referenceCode, RecommendAwardRequest request, IValidator<RecommendAwardRequest> validator,
            IRecommendAwardHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(new RecommendAwardCommand(referenceCode, request.WinningProposalCode, request.WinningProposalId, request.JustificationAr, request.JustificationEn), ct));
        })
        .RequirePermission(Permissions.AwardRecommend)
        .WithName("RecommendAward");

        group.MapPost("/route-for-approval", async (string referenceCode, IRouteAwardForApprovalHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new RouteAwardForApprovalCommand(referenceCode), ct)))
        .RequirePermission(Permissions.AwardRecommend)
        .RequireIfMatch()
                // T-030 split (4)/P12 item 26: the new version goes back on the response, so a second
        // transition on this aggregate has a precondition to send without waiting for a re-read.
        .WithFreshETag()
.WithName("RouteAwardForApproval");

        group.MapPost("/approve", async (string referenceCode, IApproveAwardHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new ApproveAwardCommand(referenceCode), ct)))
        .RequirePermission(Permissions.AwardApprove)
        .RequireIfMatch()
        .RequireIdempotencyKey()
                // T-030 split (4)/P12 item 26: the new version goes back on the response, so a second
        // transition on this aggregate has a precondition to send without waiting for a re-read.
        .WithFreshETag()
.WithName("ApproveAward");

        group.MapPost("/reject", async (
            string referenceCode, RejectAwardRequest request, IValidator<RejectAwardRequest> validator,
            IRejectAwardHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return MapMutation(await handler.HandleAsync(new RejectAwardCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.AwardReject)
        .RequireIfMatch()
                // T-030 split (4)/P12 item 26: the new version goes back on the response, so a second
        // transition on this aggregate has a precondition to send without waiting for a re-read.
        .WithFreshETag()
.WithName("RejectAward");

        group.MapPost("/execute", async (string referenceCode, IExecuteAwardHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new ExecuteAwardCommand(referenceCode), ct)))
        .RequirePermission(Permissions.AwardApprove)
        .RequireIfMatch()
                // T-030 split (4)/P12 item 26: the new version goes back on the response, so a second
        // transition on this aggregate has a precondition to send without waiting for a re-read.
        .WithFreshETag()
.WithName("ExecuteAward");

        group.MapPost("/retry-erp-sync", async (string referenceCode, IRetryErpSyncHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new RetryErpSyncCommand(referenceCode), ct)))
        .RequirePermission(Permissions.IntegrationRetry)
        .WithName("RetryAwardErpSync");
    }
}
