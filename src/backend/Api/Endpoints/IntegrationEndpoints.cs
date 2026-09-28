// The systems this product talks to: /api/v1/admin/integrations.
//
// THE SECRET GOES IN AND NEVER COMES OUT. There is no route here that reads one, and adding one later would
// undo the reason the screen is safe to use.
//
// TESTING IS A POST because it reaches out to another ministry's server, which is not something a link
// prefetcher or a cache should be free to do on somebody's behalf.
//
// A FAILED TEST STILL ANSWERS 200. The request to test worked; the answer is "no". Returning 502 would make a
// working screen look broken and would bury the detail, which is the part an administrator acts on.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Identity;

public static class IntegrationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/integrations").WithTags("Integrations");

        group.MapGet("/", async (IListIntegrationsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminIntegrationsManage)
        .WithName("ListIntegrations");

        group.MapPut("/{key}", async (
            string key,
            UpdateIntegrationRequest request,
            IUpdateIntegrationHandler handler,
            CancellationToken ct) =>
        {
            var updated = await handler.HandleAsync(key, request, ct);

            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .RequirePermission(Permissions.AdminIntegrationsManage)
        .WithName("UpdateIntegration");

        group.MapPost("/{key}/test", async (
            string key,
            ITestIntegrationHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(key, ct);

            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .RequirePermission(Permissions.AdminIntegrationsManage)
        .WithName("TestIntegration");

        return app;
    }
}
