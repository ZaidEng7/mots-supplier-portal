// The refresh cookie's set and clear lifecycle, over the wire.
//
// Written alongside the S4790 and cookie-flag notes in AuthEndpoints. Those argue that the Delete calls are
// correct because a browser matches a cookie on (name, domain, path) and the Path constant is shared with the
// Append - which makes that shared constant, not the flags, the thing logout's correctness actually rests on.
// Nothing tested it. A comment asserting a control that no test exercises is how the earlier "RowVersion
// present but inert" defects survived review, so the claim is made executable here rather than left as prose.
// If the two Path constants ever drift apart, logout returns 204 while leaving a live refresh token in the
// browser; these tests fail instead.
//
// The cookie name is bound to the production constant rather than a literal, because a test that hardcoded it
// would keep passing if the endpoint renamed the cookie and stopped clearing the real one. The account must be
// a verified one: login on an unverified account is rejected and issues no cookie, so registering alone would
// make this test assert nothing.
//
// Secure is unconditional as of the S2092 fix, and that assertion is what stops a toggle being reintroduced.
// The integration host runs over plain HTTP, so it ALSO pins the fact that emitting Secure does not depend on
// the transport - the browser-side half, that http://localhost accepts and returns a Secure cookie, was
// verified against a real browser and cannot be asserted from here.
//
// Logout clears the cookie on the same path it was set: the clearing cookie is an empty value with an expiry
// in the past, and both halves matter, because a browser only removes the cookie if it can match it and only
// if it is told it has expired.

namespace MotsSupplierPortal.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class RefreshCookieLifecycleTests(PostgresApiFixture fixture)
{
    private const string CookieName = MotsSupplierPortal.Api.Endpoints.AuthEndpoints.RefreshCookieName;

    private static IEnumerable<string> SetCookieHeadersFor(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(v => v.StartsWith(name + "=", StringComparison.Ordinal))
            : [];

    [Fact]
    public async Task Login_sets_the_refresh_cookie_with_a_path_and_protective_flags()
    {
        var (client, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(
            fixture, "Cookie Lifecycle Co");

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = SupplierTestClient.Password });

        var setCookie = SetCookieHeadersFor(login, CookieName).SingleOrDefault();
        setCookie.Should().NotBeNull("login must issue the refresh cookie");
        setCookie.Should().Contain("httponly", "the refresh token must not be readable from script")
            .And.Contain("samesite=strict");

        setCookie.Should().Contain("secure",
            "nothing in configuration may weaken transport security on the refresh token");
        setCookie.Should().Contain("path=/api/v1/auth",
            "Path is part of the (name, domain, path) triple the Delete on logout must match");
    }

    [Fact]
    public async Task Logout_clears_the_refresh_cookie_on_the_same_path_it_was_set()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Cookie Logout Co");

        var logout = await client.PostAsync("/api/v1/auth/logout", null);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var setCookie = SetCookieHeadersFor(logout, CookieName).SingleOrDefault();
        setCookie.Should().NotBeNull("logout must emit a clearing Set-Cookie, not merely return 204");

        setCookie.Should().Contain("expires=Thu, 01 Jan 1970");
        setCookie.Should().Contain("path=/api/v1/auth",
            "a Delete on a different path silently matches nothing and leaves the token live");
    }
}
