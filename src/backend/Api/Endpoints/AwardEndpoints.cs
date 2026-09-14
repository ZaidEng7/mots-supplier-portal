// The award routes: recommending a winner, routing it for approval, approving or rejecting it, issuing it,
// and retrying the finance-system sync. Each one is gated on the permission the written process names for
// that actor.
//
// The winner is named by the bid's public code. The internal identifier is still accepted, so this stays a
// purely additive change: renaming a field on the wire would force a version bump, and a request that used
// to work must keep working. The code wins when both arrive.
//
// Both identifiers carry defaults, so neither generates as required in the published schema. The contract
// check caught the alternative: a nullable field with no default still generates as required, which would
// have made the new code a new demand on every existing caller, the precise breakage this rework exists to
// avoid.
//
// Every transition puts the new version on its own response, so a second transition on the same award has a
// precondition to send without waiting for a re-read.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Awards;
using MotsSupplierPortal.Domain.Identity;

public sealed record RecommendAwardRequest(
    string JustificationAr, string JustificationEn,
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
            string referenceCode, RecommendAwardRequest request, 
            IRecommendAwardHandler handler, CancellationToken ct) =>
        {
            return MapMutation(await handler.HandleAsync(new RecommendAwardCommand(referenceCode, request.WinningProposalCode, request.WinningProposalId, request.JustificationAr, request.JustificationEn), ct));
        })
        .RequirePermission(Permissions.AwardRecommend)
        .Validate<RecommendAwardRequest>()
        .WithName("RecommendAward");

        group.MapPost("/route-for-approval", async (string referenceCode, IRouteAwardForApprovalHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new RouteAwardForApprovalCommand(referenceCode), ct)))
        .RequirePermission(Permissions.AwardRecommend)
        .RequireIfMatch()
        .WithFreshETag()
.WithName("RouteAwardForApproval");

        group.MapPost("/approve", async (string referenceCode, IApproveAwardHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new ApproveAwardCommand(referenceCode), ct)))
        .RequirePermission(Permissions.AwardApprove)
        .RequireIfMatch()
        .RequireIdempotencyKey()
        .WithFreshETag()
.WithName("ApproveAward");

        group.MapPost("/reject", async (
            string referenceCode, RejectAwardRequest request, 
            IRejectAwardHandler handler, CancellationToken ct) =>
        {
            return MapMutation(await handler.HandleAsync(new RejectAwardCommand(referenceCode, request.Reason), ct));
        })
        .RequirePermission(Permissions.AwardReject)
        .RequireIfMatch()
        .Validate<RejectAwardRequest>()
        .WithFreshETag()
.WithName("RejectAward");

        group.MapPost("/execute", async (string referenceCode, IExecuteAwardHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new ExecuteAwardCommand(referenceCode), ct)))
        .RequirePermission(Permissions.AwardApprove)
        .RequireIfMatch()
        .WithFreshETag()
.WithName("ExecuteAward");

        group.MapPost("/retry-erp-sync", async (string referenceCode, IRetryErpSyncHandler handler, CancellationToken ct) =>
            MapMutation(await handler.HandleAsync(new RetryErpSyncCommand(referenceCode), ct)))
        .RequirePermission(Permissions.IntegrationRetry)
        .WithName("RetryAwardErpSync");
    }
}
