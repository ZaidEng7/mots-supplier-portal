// When presenting a refresh token that is no longer active ends the session, and what signing out ends.
//
// Presenting any dead token used to be treated as theft: the family was revoked and a reuse row written whether the
// token had been stolen, had simply expired, had been signed out, or had lost a race with a second request from
// the same browser. The last one is ordinary use, because the interface sent one refresh per refused request, so
// two requests meeting an expired access token together signed the person out.
//
// TOKEN ROTATED A MOMENT AGO. A second refresh with the token the first one just rotated is refused, and that is
// all: the successor still refreshes, so the family was not revoked, no reuse row is stored, and the answer does
// not clear the cookie, because by the time it arrives the browser may hold the successor.
//
// TOKEN ROTATED LONG AGO. Past the grace period the same presentation is theft. The family is revoked, so the
// successor stops working too, the cookie is cleared, and exactly one reuse row is stored - the successor's own
// refusal afterwards is an ended session, not a second theft. There is no clock to inject, so the rotation is
// moved back past the grace in storage, using the handler's own grace value so the test follows it.
//
// EXPIRED, AND SIGNED OUT. Each is refused as an ended session: the cookie is cleared, no reuse row is stored,
// nothing is revoked that was not already, and the person's other session, signed in beside it, still refreshes.
// The signed-out token is presented as an old tab would present it, after the grace period has passed, because
// inside the grace a token with no successor and a token that has one are both only refused; past it, only the
// absence of a successor keeps the sign-out from being taken for theft.
//
// SIGNING OUT. It used to delete the cookie and leave the session live on the server for up to thirty days. Now it
// revokes the family, so the session leaves the active-session count an administrator sees, the cookie stops
// refreshing, and the sign-out is stored. The count is read through a staff change's read-back, the same count the
// staff list shows; re-assigning the role the account already holds revokes nothing, so only the sign-out moves
// it. The account's own setup signed in once, so there are two sessions before and one after.
//
// The whole family goes, not only the token presented: a sign-out carrying a token rotated a moment ago, as a
// request racing a refresh does, still ends the session its successor belongs to.
//
// With no cookie, or one that names no session, signing out still answers 204 and records nothing.

namespace MotsSupplierPortal.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class RefreshReuseAndSignOutTests(PostgresApiFixture fixture)
{
    private async Task<(string Email, Guid UserId)> StaffAccountAsync(string role = Roles.OnboardingReviewer)
    {
        var (_, email) = await StaffTestClient.CreateWithEmailAsync(fixture, role, null);
        return (email, await SessionSteps.UserIdAsync(fixture, email));
    }

    private async Task AlterTokenAsync(string cookie, DateTimeOffset? revokedAt = null, DateTimeOffset? expiresAt = null)
    {
        var hash = TokenHasher.Hash(SessionSteps.TokenOf(cookie));
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = await db.RefreshTokens.SingleAsync(t => t.TokenHash == hash);

        if (revokedAt is not null) token.RevokedAt = revokedAt;
        if (expiresAt is not null) db.Entry(token).Property(t => t.ExpiresAt).CurrentValue = expiresAt.Value;
        await db.SaveChangesAsync();
    }

    private async Task<DateTimeOffset> RevokedAtAsync(string cookie, string because)
    {
        var hash = TokenHasher.Hash(SessionSteps.TokenOf(cookie));
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revokedAt = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => t.RevokedAt).SingleAsync();
        revokedAt.Should().NotBeNull(because);
        return revokedAt!.Value;
    }

    [Fact]
    public async Task A_token_rotated_a_moment_ago_is_refused_without_ending_the_session()
    {
        var (email, userId) = await StaffAccountAsync();
        var (_, first) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);
        var family = await SessionSteps.FamilyOfAsync(fixture, first!);

        var (rotation, successor) = await SessionSteps.RefreshAsync(fixture, first);
        rotation.StatusCode.Should().Be(HttpStatusCode.OK);

        var (parallel, _) = await SessionSteps.RefreshAsync(fixture, first);

        parallel.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SessionSteps.SetCookieFor(parallel).Should().BeNull(
            "clearing the cookie here could delete the successor the winning request has just set");
        (await SessionSteps.UnrevokedInFamilyAsync(fixture, family)).Should().Be(1, "the successor is still live");

        var (later, _) = await SessionSteps.RefreshAsync(fixture, successor);
        later.StatusCode.Should().Be(HttpStatusCode.OK, "the session survives a parallel refresh");

        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.RefreshReuseDetected)).Should().Be(0);
    }

    [Fact]
    public async Task A_token_rotated_away_longer_ago_than_the_grace_ends_the_session_and_is_recorded()
    {
        var (email, userId) = await StaffAccountAsync();
        var (_, first) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);
        var family = await SessionSteps.FamilyOfAsync(fixture, first!);

        var (rotation, successor) = await SessionSteps.RefreshAsync(fixture, first);
        rotation.StatusCode.Should().Be(HttpStatusCode.OK);

        var rotatedAt = await RevokedAtAsync(first!, "the control: the refresh retired the first token");
        await AlterTokenAsync(first!, revokedAt: rotatedAt - RefreshTokenHandler.ParallelRefreshGrace - TimeSpan.FromSeconds(5));

        var (replay, _) = await SessionSteps.RefreshAsync(fixture, first);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SessionSteps.SetCookieFor(replay).Should().Contain("expires=Thu, 01 Jan 1970", "theft clears the cookie");
        (await SessionSteps.UnrevokedInFamilyAsync(fixture, family)).Should().Be(0, "the whole family is revoked");

        var (owner, _) = await SessionSteps.RefreshAsync(fixture, successor);
        owner.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "neither holder keeps the session");

        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.RefreshReuseDetected)).Should().Be(1,
            "the replay is recorded once; the successor refused afterwards belongs to an ended session");
    }

    [Fact]
    public async Task An_expired_token_is_refused_without_a_reuse_row_or_revoking_anything()
    {
        var (email, userId) = await StaffAccountAsync();
        var (_, expired) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);
        var (_, other) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);
        var expiredFamily = await SessionSteps.FamilyOfAsync(fixture, expired!);

        await AlterTokenAsync(expired!, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var (refused, _) = await SessionSteps.RefreshAsync(fixture, expired);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SessionSteps.SetCookieFor(refused).Should().Contain("expires=Thu, 01 Jan 1970", "an ended session's cookie is cleared");
        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.RefreshReuseDetected)).Should().Be(0);
        (await SessionSteps.UnrevokedInFamilyAsync(fixture, expiredFamily)).Should().Be(1,
            "an expired session is over already; nothing is revoked");

        var (otherRefresh, _) = await SessionSteps.RefreshAsync(fixture, other);
        otherRefresh.StatusCode.Should().Be(HttpStatusCode.OK, "the person's other session is untouched");
    }

    [Fact]
    public async Task A_signed_out_token_is_refused_without_a_reuse_row_and_the_other_session_survives()
    {
        var (email, userId) = await StaffAccountAsync();
        var (_, signedOut) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);
        var (_, other) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);

        (await SessionSteps.LogoutAsync(fixture, signedOut)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var signedOutAt = await RevokedAtAsync(signedOut!, "signing out revokes the session on the server");
        await AlterTokenAsync(signedOut!, revokedAt: signedOutAt - RefreshTokenHandler.ParallelRefreshGrace - TimeSpan.FromSeconds(5));

        var (refused, _) = await SessionSteps.RefreshAsync(fixture, signedOut);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SessionSteps.SetCookieFor(refused).Should().Contain("expires=Thu, 01 Jan 1970", "an ended session's cookie is cleared");
        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.RefreshReuseDetected)).Should().Be(0,
            "a signed-out token has no successor, so presenting it later is an ended session rather than a replay");

        var (otherRefresh, _) = await SessionSteps.RefreshAsync(fixture, other);
        otherRefresh.StatusCode.Should().Be(HttpStatusCode.OK, "signing out of one session leaves the others alone");
    }

    [Fact]
    public async Task Signing_out_ends_the_session_on_the_server_and_is_recorded()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (email, userId) = await StaffAccountAsync(Roles.Evaluator);
        var (_, cookie) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);

        async Task<int> ActiveSessionsAsync()
        {
            var readBack = await admin.PutAsJsonAsync($"/api/v1/staff/{userId}/role", new { role = Roles.Evaluator });
            readBack.StatusCode.Should().Be(HttpStatusCode.OK, await readBack.Content.ReadAsStringAsync());
            return (await readBack.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("activeSessionCount").GetInt32();
        }

        (await ActiveSessionsAsync()).Should().Be(2, "the control: the setup's sign-in and this one are both live");

        var signOut = await SessionSteps.LogoutAsync(fixture, cookie);

        signOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
        SessionSteps.SetCookieFor(signOut).Should().Contain("expires=Thu, 01 Jan 1970", "the browser still forgets it");
        (await ActiveSessionsAsync()).Should().Be(1, "the signed-out session is no longer counted as active");

        var (refresh, _) = await SessionSteps.RefreshAsync(fixture, cookie);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a copy of the cookie no longer works");

        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.Logout)).Should().Be(1);
    }

    [Fact]
    public async Task Signing_out_with_a_token_rotated_a_moment_ago_ends_the_session_its_successor_belongs_to()
    {
        var (email, _) = await StaffAccountAsync();
        var (_, first) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);
        var (rotation, successor) = await SessionSteps.RefreshAsync(fixture, first);
        rotation.StatusCode.Should().Be(HttpStatusCode.OK);

        (await SessionSteps.LogoutAsync(fixture, first)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await SessionSteps.UnrevokedInFamilyAsync(fixture, await SessionSteps.FamilyOfAsync(fixture, first!)))
            .Should().Be(0);
        var (refresh, _) = await SessionSteps.RefreshAsync(fixture, successor);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_with_no_cookie_or_an_unknown_one_answers_204_and_records_nothing()
    {
        async Task<int> LogoutRowsAsync()
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AuditLogs.CountAsync(a => a.Action == SessionAuditActions.Logout);
        }

        var before = await LogoutRowsAsync();

        (await SessionSteps.LogoutAsync(fixture, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SessionSteps.LogoutAsync(fixture, $"{MotsSupplierPortal.Api.Endpoints.AuthEndpoints.RefreshCookieName}=not-a-session"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await LogoutRowsAsync()).Should().Be(before);
    }
}
