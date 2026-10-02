// Every sign-in, refusal and session revocation leaves a stored audit row, and a refresh leaves none.
//
// The audit logger only adds a row and the caller saves it. Each handler here used to add its row and then
// either return without saving or save first and add the row after, so every one of these rows was written to
// memory and dropped with the request. The screens and the answers were all correct, which is why nothing
// noticed. So every assertion below reads the table, never the response, and each one counts rows for an account
// created by that test, so the count cannot be borrowed from another test's sign-ins.
//
// A sign-in that succeeds is stored exactly once per sign-in, checked across two sign-ins so a handler writing it
// twice is caught as well as one writing it never. The account's own setup signs in once, which is the first.
//
// The refusals are each reached the way a person reaches them: a wrong password, an account already locked, a
// wrong code from an enrolled authenticator, and a role that requires a second factor on an account with none.
// The lock is set directly, because reaching it by failing five times would make this test about the lockout
// policy rather than about the row.
//
// Revoking one session and revoking all the others are driven by the signed-in owner through the session
// routes, with the session being kept presented as the cookie, as the browser does. Revoking all also answers how
// many sessions it ended, and that counts only live ones: a sign-in whose token expired without being revoked was
// over already. The account is given one such sign-in beside two live ones, so counting every unrevoked token's
// family would answer three. That seeded token is removed afterwards.
//
// A refresh records nothing. Rotating the token is the session ticking over, not something a person did, and
// a row for every rotation would bury the rows that matter.

namespace MotsSupplierPortal.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class SessionAuditRowTests(PostgresApiFixture fixture)
{
    private async Task<(string Email, Guid UserId)> StaffAccountAsync()
    {
        var (_, email) = await StaffTestClient.CreateWithEmailAsync(fixture, Roles.OnboardingReviewer, null);
        return (email, await SessionSteps.UserIdAsync(fixture, email));
    }

    [Fact]
    public async Task A_successful_sign_in_is_recorded_exactly_once_per_sign_in()
    {
        var (email, userId) = await StaffAccountAsync();

        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.LoginSucceeded))
            .Should().Be(1, "the account's setup signed in once");

        var (again, _) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);
        again.StatusCode.Should().Be(HttpStatusCode.OK);

        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.LoginSucceeded))
            .Should().Be(2, "one row per sign-in, neither dropped nor doubled");
    }

    [Fact]
    public async Task A_wrong_password_is_recorded()
    {
        var (email, userId) = await StaffAccountAsync();

        var (refused, _) = await SessionSteps.SignInAsync(fixture, email, "NotThePassword#2026");

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.LoginFailed)).Should().Be(1);
    }

    [Fact]
    public async Task A_sign_in_to_a_locked_account_is_recorded()
    {
        var (email, userId) = await StaffAccountAsync();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            await userManager.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        var (refused, _) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);

        refused.StatusCode.Should().Be(HttpStatusCode.Locked);
        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.LoginLockedOut)).Should().Be(1);
    }

    [Fact]
    public async Task A_wrong_second_factor_code_is_recorded()
    {
        var (_, userId) = await StaffTestClient.CreateWithMfaAndIdAsync(fixture, Roles.SystemAdmin);
        var email = await EmailOfAsync(userId);

        var (refused, _) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password, totpCode: "notacode");

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("MFA_INVALID", "the control: this is the wrong-code refusal, not a password one");
        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.LoginMfaFailed)).Should().Be(1);
    }

    [Fact]
    public async Task A_role_that_requires_a_second_factor_without_one_is_refused_and_recorded()
    {
        var email = $"staff-noenrol-{Guid.NewGuid():N}@ministry.example";
        Guid userId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                FullName = "Integration Staff (not enrolled)",
                EmailConfirmed = true,
                IsActive = true,
            };
            (await userManager.CreateAsync(user, StaffTestClient.Password)).Succeeded.Should().BeTrue();
            await userManager.AddToRoleAsync(user, Roles.SystemAdmin);
            userId = user.Id;
        }

        var (refused, _) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.LoginBlockedMfaEnrollmentRequired))
            .Should().Be(1);
    }

    [Fact]
    public async Task A_password_reset_is_recorded()
    {
        var (_, userId) = await StaffAccountAsync();

        string token;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            token = await scope.ServiceProvider.GetRequiredService<ISecurityTokenService>().IssueAsync(
                userId, SecurityTokenPurpose.PasswordReset, TimeSpan.FromHours(1), CancellationToken.None);
        }

        var reset = await fixture.CreateRawClient().PostAsJsonAsync("/api/v1/auth/reset-password",
            new { token, newPassword = "ResetByLink#2026!" });

        reset.StatusCode.Should().Be(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());
        (await SessionSteps.AuditRowsAsync(fixture, userId, "password_reset")).Should().Be(1);
    }

    [Fact]
    public async Task Revoking_one_session_is_recorded()
    {
        var (email, userId) = await StaffAccountAsync();
        var (signIn, kept) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);
        var (_, other) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);
        var otherFamily = await SessionSteps.FamilyOfAsync(fixture, other!);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auth/sessions/{otherFamily}/revoke");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await SessionSteps.AccessTokenOf(signIn));
        request.Headers.Add("Cookie", kept!);
        var revoked = await fixture.CreateRawClient().SendAsync(request);

        revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SessionSteps.UnrevokedInFamilyAsync(fixture, otherFamily)).Should().Be(0, "the control: it was revoked");
        (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.SessionRevoked)).Should().Be(1);
    }

    [Fact]
    public async Task Revoking_every_other_session_is_recorded_and_counts_only_live_sessions()
    {
        var (email, userId) = await StaffAccountAsync();
        var (signIn, kept) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);
        await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);

        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                FamilyId = Guid.CreateVersion7(),
                TokenHash = $"expired-sign-in-probe-{Guid.NewGuid():N}",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-31),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await db.SaveChangesAsync();
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sessions/revoke-all");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await SessionSteps.AccessTokenOf(signIn));
            request.Headers.Add("Cookie", kept!);
            var revoked = await fixture.CreateRawClient().SendAsync(request);

            revoked.StatusCode.Should().Be(HttpStatusCode.OK);
            (await revoked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revokedCount").GetInt32().Should().Be(2,
                "the setup's sign-in and the second one were live; the expired sign-in was already over");
            (await SessionSteps.UnrevokedInFamilyAsync(fixture, await SessionSteps.FamilyOfAsync(fixture, kept!)))
                .Should().Be(1, "the control: the session that asked is kept");
            (await SessionSteps.AuditRowsAsync(fixture, userId, SessionAuditActions.SessionsRevokedAll)).Should().Be(1);
        }
        finally
        {
            await using var cleanup = fixture.Services.CreateAsyncScope();
            var db = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RefreshTokens.Where(t => t.UserId == userId && t.TokenHash.StartsWith("expired-sign-in-probe-"))
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task A_refresh_records_nothing()
    {
        var (email, userId) = await StaffAccountAsync();
        var (_, cookie) = await SessionSteps.SignInAsync(fixture, email, StaffTestClient.Password);

        var (refreshed, rotated) = await SessionSteps.RefreshAsync(fixture, cookie);

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        rotated.Should().NotBeNull().And.NotBe(cookie, "the control: the token really rotated");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.CountAsync(a => a.AggregateId == userId && a.Action.StartsWith("refresh_")))
            .Should().Be(0, "refresh_rotated is no longer written, and nothing else is written in its place");
    }

    private async Task<string> EmailOfAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.Email!).SingleAsync();
    }
}
