using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record RejectApplicationRequest(string Reason);

public sealed class RejectApplicationRequestValidator : AbstractValidator<RejectApplicationRequest>
{
    public RejectApplicationRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
}

public sealed record RequestInfoRequest(string Reason, List<string> FlaggedProfileFields, List<string> FlaggedDocumentTypeCodes);

public sealed class RequestInfoRequestValidator : AbstractValidator<RequestInfoRequest>
{
    public RequestInfoRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
        RuleFor(x => x).Must(x => x.FlaggedProfileFields.Count > 0 || x.FlaggedDocumentTypeCodes.Count > 0)
            .WithMessage("At least one section or document must be flagged.");
    }
}

/// <summary>STORY-03.2.1/03.3.1: the application-level three-way reviewer decision (approve /
/// reject / request-info) - distinct from FEAT-05.4's simpler document-level approve/reject in
/// DocumentEndpoints.cs.</summary>
public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/review").WithTags("Review");

        group.MapGet("/queue", async (IListReviewQueueHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
            .RequirePermission(Permissions.SupplierReview)
            .WithName("ListReviewQueue");

        group.MapGet("/{referenceCode}", async (
            string referenceCode,
            IGetReviewerSupplierViewHandler handler,
            CancellationToken ct) =>
        {
            var view = await handler.HandleAsync(referenceCode, ct);
            return view is null ? Results.NotFound() : Results.Ok(view);
        })
        .RequirePermission(Permissions.SupplierReview)
        .WithName("GetReviewerSupplierView");

        group.MapPost("/{referenceCode}/pickup", async (
            string referenceCode,
            IPickUpApplicationHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(referenceCode, ct);
            return result switch
            {
                ReviewDecisionResult.Success s => Results.Ok(s.Supplier),
                ReviewDecisionResult.NotFound => Results.NotFound(),
                ReviewDecisionResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierReview)
        .WithName("PickUpApplication");

        group.MapPost("/{referenceCode}/approve", async (
            string referenceCode,
            IApproveApplicationHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(referenceCode, ct);
            return result switch
            {
                ReviewDecisionResult.Success s => Results.Ok(s.Supplier),
                ReviewDecisionResult.NotFound => Results.NotFound(),
                ReviewDecisionResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierApprove)
        .WithName("ApproveApplication");

        group.MapPost("/{referenceCode}/reject", async (
            string referenceCode,
            RejectApplicationRequest request,
            IValidator<RejectApplicationRequest> validator,
            IRejectApplicationHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.HandleAsync(referenceCode, request.Reason, ct);
            return result switch
            {
                ReviewDecisionResult.Success s => Results.Ok(s.Supplier),
                ReviewDecisionResult.NotFound => Results.NotFound(),
                ReviewDecisionResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierReview)
        .WithName("RejectApplication");

        group.MapPost("/{referenceCode}/request-info", async (
            string referenceCode,
            RequestInfoRequest request,
            IValidator<RequestInfoRequest> validator,
            IRequestInfoHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var result = await handler.HandleAsync(
                new RequestInfoCommand(referenceCode, request.Reason, request.FlaggedProfileFields, request.FlaggedDocumentTypeCodes), ct);
            return result switch
            {
                ReviewDecisionResult.Success s => Results.Ok(s.Supplier),
                ReviewDecisionResult.NotFound => Results.NotFound(),
                ReviewDecisionResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierRequestInfo)
        .WithName("RequestApplicationInfo");

        app.MapGet("/api/v1/suppliers/me/active-annotation", async (
            IGetOwnActiveAnnotationHandler handler,
            CancellationToken ct) =>
        {
            var annotation = await handler.HandleAsync(ct);
            return Results.Ok(annotation);
        })
        .RequireAuthorization()
        .WithTags("Suppliers")
        .WithName("GetOwnActiveAnnotation");

        app.MapPost("/api/v1/suppliers/me/resubmit-application", async (
            IResubmitApplicationHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ct);
            return result switch
            {
                ReviewDecisionResult.Success s => Results.Ok(s.Supplier),
                ReviewDecisionResult.NotFound => Results.NotFound(),
                ReviewDecisionResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierEdit)
        .WithTags("Suppliers")
        .WithName("ResubmitApplication");
    }
}
