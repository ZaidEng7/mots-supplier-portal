using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record DeclineInvitationRequest(string? Reason);

/// <summary>FEAT-08.4/08.6/FR-INV-004/006: the supplier-facing side of Invitations - the security
/// boundary this feature exists for. Every route resolves through a real Invitation row (see
/// SupplierRfqLoader's own doc comment); a non-invited supplier gets 404 here, not a filtered
/// empty view, so there is no oracle telling them the RFQ exists at all.
///
/// <para>Permission reuses Permissions.ProposalSubmit rather than introducing a new one - it is
/// already the only RFQ-adjacent permission supplier_admin/supplier_user hold (granted for the
/// not-yet-built EPIC-09), and it already captures "this persona may interact with an invited
/// RFQ towards proposing" without inventing a second, near-duplicate grant.</para></summary>
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

        group.MapGet("/", async (ISupplierListInvitedRfqsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.ProposalSubmit)
        .WithName("SupplierListInvitedRfqs");

        group.MapGet("/{referenceCode}", async (
            string referenceCode, ISupplierGetRfqHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(referenceCode, ct)))
        .RequirePermission(Permissions.ProposalSubmit)
        .WithName("SupplierGetRfq");

        group.MapPost("/{referenceCode}/decline", async (
            string referenceCode, DeclineInvitationRequest request, ISupplierDeclineInvitationHandler handler, CancellationToken ct) =>
            MapResult(await handler.HandleAsync(new DeclineInvitationCommand(referenceCode, request.Reason), ct)))
        .RequirePermission(Permissions.ProposalSubmit)
        .WithName("SupplierDeclineInvitation");
    }
}
