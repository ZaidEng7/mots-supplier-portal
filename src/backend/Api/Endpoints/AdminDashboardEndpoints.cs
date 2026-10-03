// The system administrator's dashboard: one read answering six sections, each ok, failed or hidden on its own,
// and beside it the probe of the object store and the virus scanner that runs only when somebody asks for it.
//
// The route is gated on the permission to manage users, the same gate as the overview it is built to take over
// from, and for the same reason: the dashboard names the same actor as managing users does.
//
// Three sections need more than the route does, the ERP block admin.integrations.manage and the security and
// recent activity feeds audit.read. Those are decided inside the handler, per section, rather than here, because
// a viewer missing one of them should still get the rest of the screen rather than a refusal.
//
// The probe is a POST because it is an action rather than a read of stored state: each call contacts two services
// outside the database, and a GET is something a browser or a proxy may repeat or prefetch on its own. It carries
// the dashboard's own gate, and the operations page's storage card calls it too, from a button.

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

        app.MapPost("/api/v1/admin/dashboard/storage-probe", async (
            IProbeDashboardStorageHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithTags("Admin")
        .WithName("ProbeDashboardStorage");
    }
}
