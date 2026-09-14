// The four dashboards: the buying officer's, the approving manager's, the supplier's own, and the onboarding
// reviewer's.
//
// Every one answers not-found rather than refused when the caller has no organization. An empty dashboard
// would still assert that an organization exists and happens to be idle. Not-found asserts nothing, which is
// what an out-of-scope read is supposed to do.
//
// There is no dashboard permission in this codebase. The information architecture names one and nothing
// defines it, so each route is gated on the permission its own screen's actions need, which is what the
// persona documents already say those people hold.
//
// The officer's dashboard is gated on reading tenders, which both the officer and the manager hold and which
// the screen's own open-a-tender action needs.
//
// The manager's approval dashboard is gated on approving tenders. The screen exists to approve, and somebody
// who cannot approve has nothing to do on it. A manager holds that permission; an officer does not.
//
// The supplier's dashboard is gated on reading their own supplier record, because the screen is given to both
// of a company's roles, and reading your own company's record is the least either can do.
//
// The reviewer's dashboard has no organization dimension at all, because a supplier registers onto the
// platform rather than into a buying body, so there the permission is the scope.
//
// The date bounds are parsed here rather than bound directly, for the same reason as on the audit routes: a
// malformed bound was already refused, so it never widened the period, but it was refused as a malformed body,
// which names no field and carries no bilingual message.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Domain.Identity;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/procurement/dashboard", async (
            string? from,
            string? to,
            IProcurementDashboardHandler handler,
            CancellationToken ct) =>
        {
            if (!FilterValues.TryParseDateBound(from, out var fromBound, out var badFrom))
            {
                return FilterValues.InvalidFilterValue("from", badFrom!);
            }

            if (!FilterValues.TryParseDateBound(to, out var toBound, out var badTo))
            {
                return FilterValues.InvalidFilterValue("to", badTo!);
            }

            var dashboard = await handler.HandleAsync(fromBound, toBound, ct);
            return dashboard is null ? Results.NotFound() : Results.Ok(dashboard);
        })
        .RequirePermission(Permissions.RfqRead)
        .WithTags("Dashboards")
        .WithName("ProcurementDashboard");

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

        app.MapGet("/api/v1/review/dashboard", async (
            IReviewDashboardHandler handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.SupplierReview)
        .WithTags("Dashboards")
        .WithName("ReviewDashboard");
    }
}
