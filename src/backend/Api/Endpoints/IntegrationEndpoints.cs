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
//
// A SAVE THE CONNECTION REFUSES IS A 422 WITH ITS OWN SENTENCE - the switch that creates suppliers in the ERP turned
// on without a group, or a group longer than the ERP holds. The request was well formed; what it asked for cannot be
// saved, and the sentence says why.
//
// THE ERP'S SUPPLIER GROUPS ANSWER AS THE IMPORT DOES WHEN THE ERP CANNOT: 503 when no connection is configured or
// enabled, 502 when the ERP refused or did not answer, each with the detail. Unlike the test, this is a read the screen
// depends on to fill a list, so a failure is a failure, and the ERP's own words go with it.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Integration.Erp;

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
            return await handler.HandleAsync(key, request, ct) switch
            {
                UpdateIntegrationResult.Updated updated => Results.Ok(updated.View),
                UpdateIntegrationResult.Refused refused => Results.UnprocessableEntity(
                    new { error = "integration_settings_refused", message = refused.Message }),
                _ => Results.NotFound(),
            };
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

        group.MapGet("/{key}/supplier-groups", async (
            string key,
            IListErpSupplierGroupsHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                var groups = await handler.HandleAsync(key, ct);

                return groups is null ? Results.NotFound() : Results.Ok(groups);
            }
            catch (ErpNotConfiguredException notConfigured)
            {
                return Results.Problem(
                    title: "The ERP integration is not configured.",
                    detail: notConfigured.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (ErpRequestException failure)
            {
                return Results.Problem(
                    title: "The ERP's supplier groups could not be read.",
                    detail: failure.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .RequirePermission(Permissions.AdminIntegrationsManage)
        .WithName("ListErpSupplierGroups");

        return app;
    }
}
