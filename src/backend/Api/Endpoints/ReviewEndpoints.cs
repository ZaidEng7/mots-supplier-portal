using FluentValidation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record RejectApplicationRequest(string Reason);

/// <summary>FR-ONB-009 / BRULE-096: reason mandatory on suspend, reactivate and deactivate alike.
/// Validated here as well as in the domain - the validator gives the caller a field-level message,
/// the domain guarantee holds regardless of which entry point is used.</summary>
public sealed record SupplierLifecycleRequest(string Reason);

public sealed class SupplierLifecycleRequestValidator : AbstractValidator<SupplierLifecycleRequest>
{
    public SupplierLifecycleRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
}

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

        // MSP-77: previously any string was accepted here, which is how the reviewer UI and the
        // supplier UI ended up with two different vocabularies that overlapped on one code - a
        // reviewer could flag "registrationNumber" and the supplier's screen would never unlock
        // anything. Now the codes must be ones the enforcement actually understands.
        RuleForEach(x => x.FlaggedProfileFields)
            .Must(ProfileFieldCodes.IsKnown)
            .WithMessage(f => $"'{f}' is not a known profile field code. Valid codes: {string.Join(", ", ProfileFieldCodes.All)}.");
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

        group.MapGet("/queue", async (string? cursor, int? pageSize, bool? withCount, string? state, string? assignedTo, HttpContext httpContext, IListReviewQueueHandler handler, CancellationToken ct) =>
            ListResponse.Ok(httpContext, await handler.HandleAsync(cursor, pageSize, withCount == true, state, assignedTo, ct), pageSize))
            .RequirePermission(Permissions.SupplierReview)
            // §6.3: oldest-first is the queue's whole point - a reviewer works the backlog from the
            // end that has waited longest, so ascending createdAt is the default AND the only order.
            .WithListQuery(ListQueryPolicy.Create("createdAt", ["createdAt"], "state", "assignedTo"))
            .WithName("ListReviewQueue");

        group.MapPost("/{referenceCode}/claim", async (string referenceCode, IClaimReviewItemHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(referenceCode, ct);
            return result switch
            {
                ClaimQueueItemResult.Success s => Results.Ok(s.Item),
                ClaimQueueItemResult.NotFound => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierReview)
        .WithName("ClaimReviewItem");

        group.MapPost("/{referenceCode}/unassign", async (string referenceCode, IUnassignReviewItemHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(referenceCode, ct);
            return result switch
            {
                ClaimQueueItemResult.Success s => Results.Ok(s.Item),
                ClaimQueueItemResult.NotFound => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierReview)
        .WithName("UnassignReviewItem");

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
        .RequirePermission(Permissions.SupplierReject)
        .WithName("RejectApplication");

        // FR-ONB-009 post-approval lifecycle (MSP-63). Suspended and Deactivated were unreachable
        // enum values until now - declared, persisted, and with no way to reach them.
        //
        // 409 Conflict for an illegal transition rather than 400: the request is well-formed, it
        // conflicts with the supplier's current state, and the domain's message says which state.
        foreach (var (segment, name, invoke) in new (string, string, Func<ISupplierLifecycleHandler, SupplierLifecycleCommand, CancellationToken, Task<SupplierLifecycleResult>>)[]
        {
            ("suspend", "SuspendSupplier", (h, c, ct) => h.SuspendAsync(c, ct)),
            ("reactivate", "ReactivateSupplier", (h, c, ct) => h.ReactivateAsync(c, ct)),
            ("deactivate", "DeactivateSupplier", (h, c, ct) => h.DeactivateAsync(c, ct)),
        })
        {
            var handlerInvoke = invoke;
            group.MapPost($"/{{referenceCode}}/{segment}", async (
                string referenceCode,
                SupplierLifecycleRequest request,
                IValidator<SupplierLifecycleRequest> validator,
                ISupplierLifecycleHandler handler,
                CancellationToken ct) =>
            {
                var validation = await validator.ValidateAsync(request, ct);
                if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

                var result = await handlerInvoke(handler, new SupplierLifecycleCommand(referenceCode, request.Reason), ct);
                return result switch
                {
                    SupplierLifecycleResult.Success s => Results.Ok(new { lifecycleState = s.LifecycleState }),
                    SupplierLifecycleResult.NotFound => Results.NotFound(),
                    SupplierLifecycleResult.Invalid i => Results.Conflict(new { error = i.Message }),
                    _ => Results.Problem(),
                };
            })
            .RequirePermission(Permissions.SupplierLifecycleManage)
            .WithName(name);
        }

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
