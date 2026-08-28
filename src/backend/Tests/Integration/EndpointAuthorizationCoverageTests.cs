using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-67 / NFR-SEC-004: the guard that stops deny-by-default from silently regressing.
///
/// Enumerates the application's real endpoint table and asserts every endpoint declares its
/// authorization intent explicitly - either an authorization requirement (via .RequireAuthorization
/// or .RequirePermission) or an explicit .AllowAnonymous(). An endpoint that declares neither is a
/// failure even though the FallbackPolicy would currently deny it, because relying on the fallback
/// alone means the intent is invisible at the call site and an author cannot tell "public" from
/// "forgotten".
///
/// This lives in the integration suite rather than the NetArchTest project on purpose: Minimal API
/// authorization is endpoint metadata built at runtime by WebApplicationFactory, not a static type
/// relationship NetArchTest can inspect.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EndpointAuthorizationCoverageTests(PostgresApiFixture fixture)
{
    [Fact]
    public void Every_endpoint_declares_either_an_authorization_requirement_or_AllowAnonymous()
    {
        var dataSource = fixture.Services.GetRequiredService<EndpointDataSource>();

        var undeclared = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => !IsInfrastructureEndpoint(e))
            .Where(e =>
                e.Metadata.GetMetadata<IAuthorizeData>() is null &&
                e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])} /{e.RoutePattern.RawText?.TrimStart('/')}")
            .OrderBy(x => x)
            .ToArray();

        undeclared.Should().BeEmpty(
            "every endpoint must state its authorization intent explicitly (MSP-67). " +
            "Undeclared endpoints: " + string.Join(" | ", undeclared));
    }

    /// <summary>Framework-provided endpoints we do not author: the Hangfire dashboard mount and
    /// the OpenAPI document. Excluded by route prefix rather than by name so a business endpoint
    /// can never accidentally match.</summary>
    private static bool IsInfrastructureEndpoint(RouteEndpoint endpoint)
    {
        var route = endpoint.RoutePattern.RawText ?? string.Empty;
        return route.StartsWith("/hangfire", StringComparison.OrdinalIgnoreCase)
            || route.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase);
    }
}
