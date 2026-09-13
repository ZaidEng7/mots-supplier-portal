// The onboarding reviewer's surface: the queue, one application, the three-way decision on it, and the
// post-approval lifecycle.
//
// The three-way decision is approve, reject or ask for more information. It is distinct from the simpler
// per-document approve-or-reject, which lives with the document routes.
//
// A reason is mandatory on suspending, reinstating and deactivating alike. It is checked here as well as in
// the domain: the check here gives the caller a message against the field, and the domain's guarantee holds
// no matter which entry point is used.
//
// The flagged field codes on an information request must be ones the enforcement actually understands. Any
// string used to be accepted, which is how the reviewer's screen and the supplier's screen ended up with two
// different vocabularies overlapping on one code, so a reviewer could flag the registration number and the
// supplier's screen would never unlock anything.
//
//
// THE QUEUE'S THREE HAND-PARSED FILTERS
//
// The count flag, the state filter and the assignee filter are all parsed here rather than bound, and the
// queue's silent-widening case is the worst of the lot.
//
// An unrecognised state does not merely empty the filter: it falls through to the default set of three
// states, so a misspelt value returns the whole queue while reading as a filtered view. The assignee filter
// widens the same way in a different shape, where a value that is neither a known word nor a readable
// identifier fell out of the handler's branches having applied no filter at all, so a typo returned the
// entire queue.
//
// Oldest-first is the queue's whole point, because a reviewer works the backlog from the end that has waited
// longest, so that is both the default and the only order offered.
//
//
// PRECONDITIONS
//
// The reviewer's read of one application issues the version their decision writes require. It was added in
// the same change as those requirements, because a requirement whose precondition cannot be obtained refuses
// every caller, which is a mistake this project made once already on another resource.
//
// Every decision and every lifecycle change puts the new version on its own response, so a second change has
// a precondition to send without waiting for a re-read.
//
//
// THE LIFECYCLE
//
// Suspended and deactivated used to be unreachable: declared, stored, and with no way to arrive at either.
//
// An illegal change answers conflict rather than bad request. The request is well-formed, it conflicts with
// the supplier's current state, and the domain's own message says which state that is.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

public sealed record RejectApplicationRequest(string Reason);

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

        RuleForEach(x => x.FlaggedProfileFields)
            .Must(ProfileFieldCodes.IsKnown)
            .WithMessage(f => $"'{f}' is not a known profile field code. Valid codes: {string.Join(", ", ProfileFieldCodes.All)}.");
    }
}

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/review").WithTags("Review");

        group.MapGet("/queue", async (string? cursor, int? pageSize, string? withCount, string? state, string? assignedTo, HttpContext httpContext, IListReviewQueueHandler handler, CancellationToken ct) =>
        {
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            if (!FilterValues.IsAllowed(state, ReviewQueueFilterValues.States, out var invalidState))
            {
                return FilterValues.InvalidFilterValue("state", invalidState!);
            }

            if (!FilterValues.IsAllowedLiteralOrGuid(assignedTo, ReviewQueueFilterValues.AssigneeLiterals, out var invalidAssignee))
            {
                return FilterValues.InvalidFilterValue("assignedTo", invalidAssignee!);
            }

            return ListResponse.Ok(httpContext, await handler.HandleAsync(cursor, pageSize, FilterValues.BoolOrFalse(withCount), state, assignedTo, ct), pageSize);
        })
            .RequirePermission(Permissions.SupplierReview)
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
        .WithETag()
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
        .RequireIfMatch()
        .WithFreshETag()
.WithName("ApproveApplication");

        group.MapPost("/{referenceCode}/reject", async (
            string referenceCode,
            RejectApplicationRequest request,
            IValidator<RejectApplicationRequest> validator,
            IRejectApplicationHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

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
        .RequireIfMatch()
        .WithFreshETag()
.WithName("RejectApplication");

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
                if (!validation.IsValid) return ValidationProblems.From(validation);

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
            if (!validation.IsValid) return ValidationProblems.From(validation);

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
        .RequireIfMatch()
        .WithFreshETag()
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
        .RequireIfMatch()
        .WithFreshETag()
.WithName("ResubmitApplication");
    }
}
