using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record DeclineInvitationRequest(string? Reason);

public sealed record PostClarificationRequest(string Question);

public sealed class PostClarificationRequestValidator : AbstractValidator<PostClarificationRequest>
{
    public PostClarificationRequestValidator() => RuleFor(x => x.Question).NotEmpty().MaximumLength(4000);
}

/// <summary>FEAT-08.4/08.6/FR-INV-004/006: the supplier-facing side of Invitations - the security
/// boundary this feature exists for. Every route resolves through a real Invitation row (see
/// SupplierRfqLoader's own doc comment); a non-invited supplier gets 404 here, not a filtered
/// empty view, so there is no oracle telling them the RFQ exists at all.
///
/// <para>Permission uses Permissions.ProposalCreate (both supplier_admin and supplier_user hold
/// it, BUSINESS-PROCESSES.md §4.1's own actor column for "start proposal") rather than
/// ProposalSubmit - EPIC-09 corrected ProposalSubmit to supplier_admin-only per that same table, so
/// reusing it here would have silently locked supplier_user out of viewing/declining/asking on
/// their own invitations, which is a materially different action from submitting a
/// proposal.</para></summary>
public static class SupplierRfqEndpoints
{
    private static IResult MapResult(SupplierRfqResult result) => result switch
    {
        SupplierRfqResult.Success s => Results.Ok(s.Rfq),
        SupplierRfqResult.NotFoundOrNotInvited => Results.NotFound(),
        SupplierRfqResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        _ => Results.Problem(),
    };

    public static void MapSupplierRfqEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/suppliers/me/rfqs").WithTags("SupplierRfqs");

        group.MapGet("/", async (string? cursor, int? pageSize, bool? withCount, HttpContext httpContext, ISupplierListInvitedRfqsHandler handler, CancellationToken ct) =>
            ListResponse.Ok(httpContext, await handler.HandleAsync(cursor, pageSize, withCount == true, ct), pageSize))
        .RequirePermission(Permissions.ProposalCreate)
        // Same -createdAt divergence from §6.3's "-publishedAt" example as the buyer list, for a
        // different reason: the RFQ aggregate has no PublishedAt column at all. See RfqEndpoints.
        .WithListQuery(ListQueryPolicy.Create("-createdAt", ["createdAt"]))
        .WithName("SupplierListInvitedRfqs");

        group.MapGet("/{referenceCode}", async (
            string referenceCode, ISupplierGetRfqHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(referenceCode, ct)))
        .RequirePermission(Permissions.ProposalCreate)
        .WithName("SupplierGetRfq");

        group.MapPost("/{referenceCode}/decline", async (
            string referenceCode, DeclineInvitationRequest request, ISupplierDeclineInvitationHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(new DeclineInvitationCommand(referenceCode, request.Reason), ct)))
        .RequirePermission(Permissions.ProposalCreate)
        .WithName("SupplierDeclineInvitation");

        group.MapPost("/{referenceCode}/clarifications", async (
            string referenceCode,
            PostClarificationRequest request,
            IValidator<PostClarificationRequest> validator,
            ISupplierPostClarificationHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            return MapResult(await handler.HandleAsync(new PostClarificationQuestionCommand(referenceCode, request.Question), ct));
        })
        .RequirePermission(Permissions.ProposalCreate)
        .WithName("SupplierPostClarification");
    }
}
