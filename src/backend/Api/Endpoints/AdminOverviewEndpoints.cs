using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// T-062/FR-DSH-006/SCR-700: the admin dashboard. <c>system_admin</c> had no landing page - the
/// persona could authenticate and had nowhere to go.
/// </summary>
public static class AdminOverviewEndpoints
{
    public static void MapAdminOverviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/overview", async (
            IGetAdminOverviewHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        // AdminUsersManage, which this catalogue documents as system_admin-only. Not a new permission:
        // FR-DSH-006 names the same actor as FR-ADM-002, and a dashboard over users, jobs and the
        // outbox is not a narrower authority than managing users.
        .RequirePermission(Permissions.AdminUsersManage)
        .WithTags("Admin")
        .WithName("GetAdminOverview");
    }
}
