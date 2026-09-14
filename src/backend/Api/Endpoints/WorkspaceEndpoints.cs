// The guided workspace: one read-only view that gathers everything about a tender in one place.
//
// It is gated on the same permission the tender's own detail route uses as its broad "may view this
// tender" gate, because the workspace is a view over that same tender rather than a new resource with
// visibility rules of its own.
//
// That shared gate used to be the authoring permission, which locked a procurement manager out of the
// workspace for the same reason it locked them out of the tender list. The workspace moves with the gate
// it was deliberately tied to.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Workspace;
using MotsSupplierPortal.Domain.Identity;

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
