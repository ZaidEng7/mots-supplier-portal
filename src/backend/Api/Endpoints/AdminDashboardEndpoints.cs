// The system administrator's dashboard: one read answering six sections, each ok, failed or hidden on its own.
//
// The route is gated on the permission to manage users, the same gate as the overview it is built to take over
// from, and for the same reason: the dashboard names the same actor as managing users does.
//
// Three sections need more than the route does, the ERP block admin.integrations.manage and the security and
// recent activity feeds audit.read. Those are decided inside the handler, per section, rather than here, because
// a viewer missing one of them should still get the rest of the screen rather than a refusal.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Domain.Identity;

public static class AdminDashboardEndpoints
{
    public static void MapAdminDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/dashboard", async (
            IGetAdminDashboardHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithTags("Admin")
        .WithName("GetAdminDashboard");
    }
}
