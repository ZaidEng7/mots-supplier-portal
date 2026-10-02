// The steps a browser takes with its refresh cookie, driven over real HTTP, for the session tests.
//
// The cookie is carried by hand on each request rather than left to a cookie jar. It is issued Secure and the test
// host speaks plain HTTP, so a jar would store it and never send it back; carrying it by hand is what the change
// password and lifecycle tests already do. Each request goes out on a fresh client for the same reason: no jar
// state can leak from one step into the next and present a cookie the test did not choose.
//
// A clearing Set-Cookie, an empty value with an expiry in the past, is reported as no cookie rather than as an
// empty one, so a step that ended the session reads as having handed nothing back.
//
// The audit counts read storage, never the response, because the defect these tests exist for was rows that were
// added and never saved: every answer looked right and the table stayed empty. They count only rows filed under the
// person and naming that same person as the actor, because every session row is something that person did, and a
// row stored with no actor or the wrong one answers nothing an investigation asks.
//
// The concurrent steps send their requests through one client each, released together, so the server receives
// them as close to the same instant as a browser's parallel requests arrive.

namespace MotsSupplierPortal.Tests.Integration.Auth;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Api.Endpoints;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class SessionSteps
{
    public static async Task<(HttpResponseMessage Response, string? Cookie)> SignInAsync(
        PostgresApiFixture fixture, string email, string password, string? totpCode = null)
    {
        var response = await fixture.CreateRawClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password, totpCode });
        return (response, CookieOf(response));
    }

    public static async Task<string> AccessTokenOf(HttpResponseMessage signIn) =>
        (await signIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

    public static async Task<(HttpResponseMessage Response, string? Cookie)> RefreshAsync(PostgresApiFixture fixture, string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);

        var response = await fixture.CreateRawClient().SendAsync(request);
        return (response, CookieOf(response));
    }

    public static async Task<HttpResponseMessage> LogoutAsync(PostgresApiFixture fixture, string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);

        return await fixture.CreateRawClient().SendAsync(request);
    }

    public static async Task<HttpResponseMessage[]> AtOnceAsync(params Func<Task<HttpResponseMessage>>[] sends)
    {
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = sends.Select(async send =>
        {
            await go.Task;
            return await send();
        }).ToList();

        go.SetResult();
        return await Task.WhenAll(sent);
    }

    public static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return null;

        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static string? CookieOf(HttpResponseMessage response)
    {
        var pair = SetCookieFor(response)?.Split(';')[0];
        return pair is null || pair == AuthEndpoints.RefreshCookieName + "=" ? null : pair;
    }

    public static string? SetCookieFor(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(AuthEndpoints.RefreshCookieName + "=", StringComparison.Ordinal))
            : null;

    public static string TokenOf(string cookie) => cookie[(AuthEndpoints.RefreshCookieName.Length + 1)..];

    public static async Task<Guid> UserIdAsync(PostgresApiFixture fixture, string email)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    public static async Task<int> AuditRowsAsync(PostgresApiFixture fixture, Guid userId, string action)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.CountAsync(a => a.AggregateId == userId && a.ActorUserId == userId && a.Action == action);
    }

    public static async Task<Guid> FamilyOfAsync(PostgresApiFixture fixture, string cookie)
    {
        var hash = TokenHasher.Hash(TokenOf(cookie));
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => t.FamilyId).SingleAsync();
    }

    public static async Task<int> UnrevokedInFamilyAsync(PostgresApiFixture fixture, Guid familyId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RefreshTokens.CountAsync(t => t.FamilyId == familyId && t.RevokedAt == null);
    }
}
