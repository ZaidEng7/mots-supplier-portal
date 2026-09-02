using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Workspace;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>FEAT-13.1/FR-PWF-001: the guided-workspace read model. Gated on the same
/// <see cref="Permissions.RfqRead"/> claim RfqEndpoints' own GET /{referenceCode} uses as its
/// broad "can view this RFQ" gate - the workspace is a read-side view over that same RFQ, not a new
/// resource with its own visibility rules. That shared gate was rfq.create until this batch, which
/// locked procurement_manager out of the workspace for the same reason it locked them out of the
/// list; the workspace moves with the gate it was deliberately tied to.</summary>
public static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/rfqs/{referenceCode}/workspace", async (string referenceCode, IGetWorkspaceHandler handler, CancellationToken ct) =>
        {
            var workspace = await handler.HandleAsync(referenceCode, ct);
            return workspace is null ? Results.NotFound() : Results.Ok(workspace);
        })
        .RequirePermission(Permissions.RfqRead)
        .WithTags("Workspace")
        .WithName("GetWorkspace");
    }
}
