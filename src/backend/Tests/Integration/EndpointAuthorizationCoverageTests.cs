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

        // THE DENOMINATOR, asserted before the rule (Phase 4 sweep, MSP-83).
        //
        // This test passes when `undeclared` is empty - and `undeclared` is also empty if the
        // endpoint data source returned nothing, or if IsInfrastructureEndpoint grew broad enough to
        // exclude everything. Both would render as the same green as genuine success, on the test
        // that guarantees MSP-67's deny-by-default intent is stated at every endpoint.
        //
        // Six instruments in this repository have already been found reporting over an empty or
        // absent set - the sixth found while closing Phase 4 (Task #12/#13, .github/workflows/ci.yml):
        // the whole-project Sonar gate always read main's own state, never the PR's, so a PR could
        // not move the number it was ostensibly being judged on. Same shape as the other five,
        // just at the level of a CI gate instead of a query: a whole-project metric that cannot
        // move in response to what is actually being reviewed is not a PR gate, however it is
        // labeled - it measures something, just never the thing in front of the reader. Decided
        // to keep it reporting-only rather than wire a blocking check to it, in favor of the
        // project's own new-code coverage ratchet, which IS PR-specific and already proven. A
        // security-coverage test is the worst possible candidate to be the seventh.
        var examined = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => !IsInfrastructureEndpoint(e))
            .ToList();

        var excluded = dataSource.Endpoints.OfType<RouteEndpoint>().Count() - examined.Count;

        examined.Should().HaveCountGreaterThan(40,
            $"this asserts authorization intent across the authored API surface, and only " +
            $"{examined.Count} endpoints were examined - the application maps far more than that, " +
            "so an empty or truncated set means the test is passing over nothing");

        excluded.Should().BeLessThan(10,
            $"IsInfrastructureEndpoint is meant to skip a handful of framework-provided mounts, " +
            $"but it excluded {excluded} endpoints. A filter that grows broad enough to swallow " +
            "authored endpoints turns this test green by removing its subject");

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
