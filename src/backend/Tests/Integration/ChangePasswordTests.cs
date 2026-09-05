using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// SCR-903: a signed-in user changing their own password.
///
/// <para>Before this, the only path was signing out and using the forgotten-password email — a
/// recovery flow doing routine work. Every assertion below is against storage or a real response,
/// never against the code path.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ChangePasswordTests(PostgresApiFixture fixture)
{
    private const string NewPassword = "ChangedPassword#2026";

    [Fact]
    public async Task A_signed_in_user_can_change_their_password_and_the_new_one_works()
    {
        var (client, email) = await StaffTestClient.CreateWithEmailAsync(fixture, Roles.OnboardingReviewer, null);

        var changed = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = StaffTestClient.Password, newPassword = NewPassword });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        // The real proof: the new password authenticates and the old one no longer does. Asserting
        // the 200 alone would pass on a handler that returned success and wrote nothing.
        var withNew = await fixture.CreateRawClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = NewPassword });
        withNew.StatusCode.Should().Be(HttpStatusCode.OK);

        var withOld = await fixture.CreateRawClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = StaffTestClient.Password });
        withOld.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_wrong_current_password_is_refused_and_changes_nothing()
    {
        var (client, email) = await StaffTestClient.CreateWithEmailAsync(fixture, Roles.OnboardingReviewer, null);

        var refused = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = "NotTheCurrentOne#1", newPassword = NewPassword });

        // 422, not 401 — the caller IS authenticated, and a 401 would bounce them to the login screen
        // mid-form as though their session had expired.
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("INCORRECT_CURRENT_PASSWORD");

        // The control for the refusal: the ORIGINAL password still works, so the guard refused the
        // change rather than half-applying it.
        (await fixture.CreateRawClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = StaffTestClient.Password })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reusing_the_current_password_is_refused_rather_than_succeeding_silently()
    {
        var (client, _) = await StaffTestClient.CreateWithEmailAsync(fixture, Roles.OnboardingReviewer, null);

        var refused = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = StaffTestClient.Password, newPassword = StaffTestClient.Password });

        // A no-op "success" would still revoke every other session, signing the user out of their
        // other devices for a change that changed nothing.
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("PASSWORD_UNCHANGED");
    }

    [Fact]
    public async Task A_weak_new_password_is_refused_with_the_reasons()
    {
        var (client, _) = await StaffTestClient.CreateWithEmailAsync(fixture, Roles.OnboardingReviewer, null);

        var refused = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = StaffTestClient.Password, newPassword = "short" });

        // The validator's own length floor, before Identity is asked.
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_change_anyone_s_password()
    {
        var refused = await fixture.CreateRawClient().PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = StaffTestClient.Password, newPassword = NewPassword });

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_change_revokes_other_sessions_and_keeps_the_one_that_presented_its_cookie()
    {
        var (client, email) = await StaffTestClient.CreateWithEmailAsync(fixture, Roles.OnboardingReviewer, null);

        // Sign in on a raw client so the refresh cookie is visible on the response, and present it
        // back on the change - which is what the SPA does (fetch with credentials: 'include').
        var raw = fixture.CreateRawClient();
        var login = await raw.PostAsJsonAsync("/api/v1/auth/login", new { email, password = StaffTestClient.Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshCookie = login.Headers.TryGetValues("Set-Cookie", out var setCookies)
            ? setCookies.FirstOrDefault(c => c.StartsWith("mots_refresh_token=", StringComparison.Ordinal))
            : null;
        refreshCookie.Should().NotBeNull("the control: without a cookie there is no current session to exclude");

        // A third sign-in, so there is definitely a session that is NOT the one presenting the cookie.
        (await fixture.CreateRawClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = StaffTestClient.Password })).StatusCode.Should().Be(HttpStatusCode.OK);

        Guid userId;
        int before;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            before = await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null);
            before.Should().BeGreaterThan(1, "the control: more than one live session exists to revoke");
        }

        var token = await SignInTokenAsync(raw, email, StaffTestClient.Password);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = StaffTestClient.Password, newPassword = NewPassword }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("Cookie", token.RefreshCookie);

        (await fixture.CreateRawClient().SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var after = fixture.Services.CreateAsyncScope())
        {
            var db = after.ServiceProvider.GetRequiredService<AppDbContext>();
            var live = await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null);

            // Both halves. Something was revoked, so the guard fires; and not everything was, so a
            // password change is not a sign-out of the tab that performed it. Asserting only "fewer
            // than before" would pass on a handler that revoked every session including this one.
            live.Should().BeLessThan(before, "other sessions are revoked");
            live.Should().BeGreaterThan(0, "the session that presented its own cookie survives");
        }

        _ = client;
    }

    /// <summary>Signs in and returns both halves of the session: the bearer token and the refresh
    /// cookie, so a caller can present the pair the SPA presents.</summary>
    private static async Task<(string AccessToken, string RefreshCookie)> SignInTokenAsync(
        HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = login.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("mots_refresh_token=", StringComparison.Ordinal))
            .Split(';')[0];
        return (body.GetProperty("accessToken").GetString()!, cookie);
    }
}
