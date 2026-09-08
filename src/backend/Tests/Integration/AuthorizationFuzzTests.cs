using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// P12/NFR-SEC-004: every permissioned route, against every persona that does not hold its permission.
///
/// <para><b>What the existing instruments do and do not cover.</b>
/// <c>EndpointAuthorizationCoverageTests</c> asserts that each endpoint DECLARES an intent - a permission or
/// an explicit AllowAnonymous - which is a static property of the endpoint table.
/// <c>EndpointAuthorizationGapTests</c> pins two specific holes found by hand. Neither one sends a request as
/// the wrong persona, so neither can catch a filter that is declared and does not fire: the
/// <c>AllowAnonymous</c>-beats-<c>RequireAuthorization</c> defect it records was exactly that shape, and it
/// was found by a person trying it, not by the suite.</para>
///
/// <para><b>This sweep sends the requests.</b> For each route and each of the eight seeded roles that lacks
/// its permission, it calls the route and requires a refusal. Only the roles that should be REFUSED are
/// called, which is what makes fuzzing the real application safe: no request in this file is one the server
/// ought to carry out, so nothing here can mutate a row.</para>
///
/// <para><b>A 5xx is a failure too, and that is half the value.</b> A route that throws for a caller it was
/// going to refuse has done work before its gate - bound a body, hit the database, resolved a handler - and a
/// crash there is both a leak of behaviour and a denial-of-service surface. The assertion is therefore "not
/// 2xx and not 5xx", not merely "not 2xx".</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuthorizationFuzzTests(PostgresApiFixture fixture)
{
    /// <summary>The eight seeded personas. Every one is a real client with a real token, because a
    /// hand-built claims principal would test this test's idea of a role rather than the seed's.</summary>
    private static readonly string[] Personas =
    [
        Roles.SupplierAdmin, Roles.SupplierUser, Roles.OnboardingReviewer, Roles.ProcurementOfficer,
        Roles.ProcurementManager, Roles.Evaluator, Roles.MinistryViewer, Roles.SystemAdmin,
    ];

    /// <summary>
    /// Methods this sweep sends. GET and DELETE carry no body, so the request reaches the permission filter
    /// without a binding failure standing in for the refusal.
    ///
    /// <para>POST and PUT are deliberately NOT sent: a Minimal API binds arguments before endpoint filters
    /// run, so a bodyless POST is answered 400 by model binding and the assertion would pass without the
    /// permission check ever executing - a green that means nothing, which is the failure mode this
    /// repository keeps finding in its own instruments. Their gate is the same filter instance, and
    /// EndpointAuthorizationCoverageTests proves every one of them declares it.</para>
    /// </summary>
    private static readonly string[] SafeMethods = ["GET", "DELETE"];

    /// <summary>A concrete URL for a route template, with every parameter replaced by a value that cannot
    /// exist. The point is the refusal: a caller without the permission must never learn whether the id is
    /// real, so a sentinel id is the correct probe and a 404 would itself be a finding.</summary>
    private static string? ConcreteUrl(string template)
    {
        var segments = template.Trim('/').Split('/');
        var url = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            if (!segment.StartsWith('{'))
            {
                url.Add(segment);
                continue;
            }

            // A route constraint tells us what shape the sentinel has to take, and getting it wrong would
            // produce a 404 from ROUTING rather than a refusal from the gate.
            if (segment.Contains(":guid", StringComparison.Ordinal))
            {
                url.Add("00000000-0000-0000-0000-0000000000ff");
            }
            else if (segment.Contains(":int", StringComparison.Ordinal))
            {
                url.Add("999999");
            }
            else if (segment.Contains("**", StringComparison.Ordinal))
            {
                // A catch-all. Not probed: the shape of what belongs there is the route's own business.
                return null;
            }
            else
            {
                url.Add("zz-does-not-exist");
            }
        }

        return "/" + string.Join('/', url);
    }

    [Fact]
    public async Task No_route_serves_a_persona_that_lacks_its_permission()
    {
        var endpoints = fixture.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<RequiredPermissionsMetadata>() is not null)
            .ToList();

        // The denominator, before the rule. An empty set passes every assertion below in silence, which is
        // the shape six of this repository's own instruments were found in.
        endpoints.Should().HaveCountGreaterThan(100,
            "the metadata must be on the permissioned routes for this sweep to be sweeping anything");

        // system_admin is excluded, for two reasons that point the same way: it holds every permission by
        // construction (Permissions.All), so there is no route it should be refused; and signing one in needs
        // the MFA step, which CreateAsync does not perform - it answers 403 and the sweep would be probing a
        // half-authenticated client rather than a persona.
        var probed = Personas.Where(role => role != Roles.SystemAdmin).ToList();

        var clients = new Dictionary<string, HttpClient>(StringComparer.Ordinal);
        foreach (var role in probed)
        {
            clients[role] = await StaffTestClient.CreateAsync(fixture, role);
        }

        // What each role holds NOW, read from the database rather than from Roles.DefaultPermissions.
        //
        // The seed is where a role STARTS; permissions are admin-editable (FR-ADM-002) and other suites edit
        // them. ReportEndpointsTests grants report.read to procurement_officer and does not take it back, so
        // a sweep expecting the seeded set reported GET /reports/compliance as a hole - the route was
        // serving a caller who genuinely held the permission by then. That false positive is the whole
        // argument for reading the live claims: "a persona that lacks this permission" has to mean lacks it
        // at the moment of the request, or the sweep is asserting against a constant the product does not use.
        var heldByRole = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            foreach (var role in probed)
            {
                var appRole = await roleManager.FindByNameAsync(role);
                var claims = appRole is null ? [] : await roleManager.GetClaimsAsync(appRole);
                heldByRole[role] = claims.Where(c => c.Type == "perms").Select(c => c.Value)
                    .ToHashSet(StringComparer.Ordinal);
            }
        }

        heldByRole.Values.Should().OnlyContain(held => held.Count > 0,
            "a role holding nothing would make every probe below a refusal for the wrong reason");

        var served = new List<string>();
        var crashed = new List<string>();
        var probes = 0;

        foreach (var endpoint in endpoints)
        {
            var required = endpoint.Metadata.GetMetadata<RequiredPermissionsMetadata>()!.Permissions;
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var url = ConcreteUrl(endpoint.RoutePattern.RawText!);
            if (url is null) continue;

            foreach (var method in methods.Where(SafeMethods.Contains))
            {
                foreach (var role in probed)
                {
                    // Only the personas that should be refused. Calling one that IS permitted would be this
                    // test performing real work against real data - a DELETE among them - which is not a
                    // thing an authorisation sweep may do as a side effect.
                    if (required.Any(heldByRole[role].Contains)) continue;

                    probes++;
                    using var request = new HttpRequestMessage(new HttpMethod(method), url);
                    var response = await clients[role].SendAsync(request);
                    var what = $"{method} {endpoint.RoutePattern.RawText} as {role} (needs {string.Join('|', required)})";

                    if ((int)response.StatusCode is >= 200 and < 300) served.Add(what);
                    if ((int)response.StatusCode >= 500) crashed.Add($"{what} -> {(int)response.StatusCode}");
                }
            }
        }

        probes.Should().BeGreaterThan(200, "a handful of probes would not be a sweep");

        served.Should().BeEmpty(
            "these routes served a persona that holds none of their permissions:\n  "
            + string.Join("\n  ", served.Take(40)));

        crashed.Should().BeEmpty(
            "these routes threw for a caller they were going to refuse, which means work happened before "
            + "the gate:\n  " + string.Join("\n  ", crashed.Take(40)));
    }

    [Fact]
    public async Task No_permissioned_route_answers_an_anonymous_caller()
    {
        // The other half, and the one with a precedent: AllowAnonymous on a group silently overrode
        // RequireAuthorization on its routes, so GET /auth/sessions answered 200 with no token at all. That
        // was found by a person trying it. This tries all of them.
        var endpoints = fixture.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<RequiredPermissionsMetadata>() is not null)
            .ToList();

        endpoints.Should().HaveCountGreaterThan(100);

        var anonymous = fixture.CreateRawClient();
        var answered = new List<string>();
        var probes = 0;

        foreach (var endpoint in endpoints)
        {
            var url = ConcreteUrl(endpoint.RoutePattern.RawText!);
            if (url is null) continue;

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            foreach (var method in methods.Where(SafeMethods.Contains))
            {
                probes++;
                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                var response = await anonymous.SendAsync(request);

                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    answered.Add($"{method} {endpoint.RoutePattern.RawText} -> {(int)response.StatusCode}");
                }
            }
        }

        probes.Should().BeGreaterThan(50);
        answered.Should().BeEmpty(
            "a permissioned route must answer an anonymous caller with 401 and nothing else - not a 403, "
            + "not a 404, and certainly not a body:\n  " + string.Join("\n  ", answered.Take(40)));
    }

    [Fact]
    public void The_probe_builder_produces_urls_a_route_can_match()
    {
        // The control on the machinery. A ConcreteUrl that returned nonsense would make both sweeps above
        // pass by 404ing at the router, before any gate ran - green, and measuring routing.
        ConcreteUrl("/api/v1/rfqs/{referenceCode}").Should().Be("/api/v1/rfqs/zz-does-not-exist");
        ConcreteUrl("/api/v1/rfqs/{referenceCode}/items/{itemId:guid}")
            .Should().Be("/api/v1/rfqs/zz-does-not-exist/items/00000000-0000-0000-0000-0000000000ff");
        ConcreteUrl("/api/v1/suppliers/me").Should().Be("/api/v1/suppliers/me");
        ConcreteUrl("/api/v1/files/{**path}").Should().BeNull("a catch-all's shape is the route's own business");
    }
}
