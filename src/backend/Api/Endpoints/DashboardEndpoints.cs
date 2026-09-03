using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// EPIC-17's read surfaces: SCR-400, SCR-401 and SCR-300.
///
/// <para><b>Every one returns 404, not 403, when the caller has no organization</b> (§9.2). An empty
/// dashboard would still assert that an organization exists and happens to be idle; a 404 asserts
/// nothing, which is what an out-of-scope read is supposed to do.</para>
///
/// <para>There is no <c>dashboard.read</c> permission in this codebase - IA §4.3 names one and
/// nothing defines it. Each endpoint is gated on the permission its own screen's actions need, which
/// is what §10 and SCREEN-INVENTORY already say those personas hold.</para>
/// </summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        // SCR-400. §10's persona list is "procurement_officer, procurement_manager", and rfq.read is
        // what both hold and what the screen's own "open RFQ" action needs.
        app.MapGet("/api/v1/procurement/dashboard", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            IProcurementDashboardHandler handler,
            CancellationToken ct) =>
        {
            var dashboard = await handler.HandleAsync(from, to, ct);
            return dashboard is null ? Results.NotFound() : Results.Ok(dashboard);
        })
        .RequirePermission(Permissions.RfqRead)
        .WithTags("Dashboards")
        .WithName("ProcurementDashboard");

        // SCR-401. Gated on rfq.approve: the screen exists to approve, and a persona who cannot
        // approve has nothing to do on it. A manager holds it; an officer does not.
        app.MapGet("/api/v1/procurement/approvals", async (
            IApprovalQueuesHandler handler,
            CancellationToken ct) =>
        {
            var queues = await handler.HandleAsync(ct);
            return queues is null ? Results.NotFound() : Results.Ok(queues);
        })
        .RequirePermission(Permissions.RfqApprove)
        .WithTags("Dashboards")
        .WithName("ApprovalQueues");

        // SCR-120. The supplier's own dashboard, scoped to their SupplierId.
        //
        // Gated on supplier.read rather than a dashboard permission: SCREEN-SPECIFICATIONS §1 gives
        // this screen to supplier_admin AND supplier_user ("delegated - sees only permitted
        // widgets"), and reading your own supplier record is the least either can do.
        app.MapGet("/api/v1/suppliers/me/dashboard", async (
            ISupplierDashboardHandler handler,
            CancellationToken ct) =>
        {
            var dashboard = await handler.HandleAsync(ct);
            return dashboard is null ? Results.NotFound() : Results.Ok(dashboard);
        })
        .RequireAuthorization()
        .WithTags("Dashboards")
        .WithName("SupplierDashboard");

        // SCR-300. Onboarding review has no organization dimension - a supplier onboards onto the
        // platform, not into a buying entity - so the permission IS the scope here.
        app.MapGet("/api/v1/review/dashboard", async (
            IReviewDashboardHandler handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.SupplierReview)
        .WithTags("Dashboards")
        .WithName("ReviewDashboard");
    }
}
