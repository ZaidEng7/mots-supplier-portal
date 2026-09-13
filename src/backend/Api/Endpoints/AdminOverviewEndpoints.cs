// The system administrator's landing page: a read-only overview of users, jobs and the outbox.
//
// It exists because that persona had no landing page at all and could sign in with nowhere to go.
//
// It is gated on the permission to manage users, which is system-administrator-only. That is not a new
// authority: the requirement for this dashboard names the same actor as the one for managing users, and
// an overview of users, jobs and the outbox is not a narrower authority than managing them.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Identity;

public static class AdminOverviewEndpoints
{
    public static void MapAdminOverviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/overview", async (
            IGetAdminOverviewHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithTags("Admin")
        .WithName("GetAdminOverview");
    }
}
