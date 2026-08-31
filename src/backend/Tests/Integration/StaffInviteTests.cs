using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Task #28/FR-ADM-001: the in-product staff invite flow, driven through the real HTTP contract
/// exactly like OrganizationEndpointTests does for admin.organizations.manage.
///
/// Two things this file deliberately does NOT do, both matching this codebase's own established
/// convention (EmailJobBehaviourTests): it never waits on the real Hangfire background worker to
/// actually process an enqueued job (the test host doesn't run one, and a live dev-server worker
/// happening to be up would make this suite flaky/non-hermetic), and it never asserts a
/// plaintext token was in the email body - EmailJobs deliberately mints the token AT SEND TIME,
/// so testing the hashed-not-plaintext property means invoking EmailJobs.SendStaffInviteEmailAsync
/// directly (as EmailJobBehaviourTests does for the other token-bearing emails), not waiting for
/// the enqueue to be dequeued by a worker this test process doesn't control.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StaffInviteTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminClientAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    private static string? TokenIn(string body)
    {
        var match = Regex.Match(body, @"token=([A-Za-z0-9_\-]+)");
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }

    [Fact]
    public async Task Invite_creates_a_staff_account_with_an_unusable_password_and_the_requested_role()
    {
        var admin = await AdminClientAsync();
        var email = $"invitee-{Guid.NewGuid():N}@ministry.example";

        var response = await admin.PostAsJsonAsync("/api/v1/staff/invite", new
        {
            email,
            fullName = "Invited Reviewer",
            role = Roles.OnboardingReviewer,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("email").GetString().Should().Be(email);
        created.GetProperty("role").GetString().Should().Be(Roles.OnboardingReviewer);
        var userId = created.GetProperty("userId").GetGuid();

        await using var scope = fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        user.Should().NotBeNull();
        user!.EmailConfirmed.Should().BeTrue("the invite itself, sent to a real address, proves control of the inbox");
        user.IsActive.Should().BeTrue();
        (await userManager.IsInRoleAsync(user, Roles.OnboardingReviewer)).Should().BeTrue();

        // The account exists but is not usable with any password the caller could guess or was
        // ever told - only AcceptStaffInviteHandler, gated by the token, can set a real one.
        (await userManager.CheckPasswordAsync(user, "whatever-a-caller-might-try")).Should().BeFalse();
    }

    [Fact]
    public async Task Invite_rejects_a_caller_without_admin_users_manage()
    {
        // onboarding_reviewer has real permissions but not admin.users.manage - proves this is a
        // genuine authorization check, not "any authenticated staff user can do this" (same shape
        // as OrganizationEndpointTests.Create_organization_rejects_a_caller_without_the_permission).
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var response = await reviewer.PostAsJsonAsync("/api/v1/staff/invite", new
        {
            email = $"nope-{Guid.NewGuid():N}@ministry.example",
            fullName = "Should Not Be Created",
            role = Roles.OnboardingReviewer,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invite_rejects_an_unauthenticated_caller()
    {
        var anonymous = fixture.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/staff/invite", new
        {
            email = $"anon-{Guid.NewGuid():N}@ministry.example",
            fullName = "Should Not Be Created",
            role = Roles.OnboardingReviewer,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Invite_refuses_a_supplier_side_role()
    {
        // supplier_admin/supplier_user come from supplier registration or the supplier-side team
        // invite, not this staff-only flow - an account made here never gets a SupplierId, so
        // granting a supplier role through it would be a role/scope mismatch by construction.
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/staff/invite", new
        {
            email = $"wrongrole-{Guid.NewGuid():N}@ministry.example",
            fullName = "Wrong Role",
            role = Roles.SupplierAdmin,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("invalid_role");
    }

    [Fact]
    public async Task Invite_refuses_a_duplicate_email()
    {
        var admin = await AdminClientAsync();
        var email = $"dup-{Guid.NewGuid():N}@ministry.example";
        await admin.PostAsJsonAsync("/api/v1/staff/invite", new { email, fullName = "First", role = Roles.OnboardingReviewer });

        var second = await admin.PostAsJsonAsync("/api/v1/staff/invite", new { email, fullName = "Second", role = Roles.ProcurementOfficer });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>MSP-61-shaped property: the token in the invite email must not be recoverable from
    /// what's persisted. Invokes EmailJobs directly (see class doc comment for why) - the same
    /// pattern EmailJobBehaviourTests uses for the other token-bearing emails.
    ///
    /// The user is seeded directly via UserManager rather than through the real POST /invite
    /// endpoint: Program.cs registers a real, unconditional Hangfire server
    /// (AddHangfireServer()), so the WebApplicationFactory test host actually runs one in-process
    /// against the same Testcontainers Postgres - a real HTTP invite call gets its enqueued job
    /// picked up and processed by that worker independently of this test's own direct call,
    /// minting a SECOND SecurityToken row for the same user+purpose (found empirically: this test
    /// failed "Sequence contains more than one element" against db.SecurityTokens.SingleAsync
    /// until switched to seeding directly). EmailJobBehaviourTests's own equivalent tests don't
    /// hit this because they assert against their local CapturingSender's own list, never against
    /// a DB row count - so the same race exists there too, just invisible to what they check.</summary>
    [Fact]
    public async Task The_invite_email_s_token_is_hashed_at_rest_and_resolves_back_to_the_invited_user()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"hashcheck-{Guid.NewGuid():N}@ministry.example";
        var seedUser = new AppUser { Id = Guid.CreateVersion7(), UserName = email, Email = email, FullName = "Hash Check", EmailConfirmed = true, IsActive = true };
        (await userManager.CreateAsync(seedUser, "SeedOnlyPassword#2026!")).Succeeded.Should().BeTrue();
        await userManager.AddToRoleAsync(seedUser, Roles.OnboardingReviewer);
        var userId = seedUser.Id;

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ISecurityTokenService>();
        var sender = new CapturingSender();
        var jobs = new EmailJobs(sender, db, tokens, scope.ServiceProvider.GetRequiredService<IConfiguration>());

        await jobs.SendStaffInviteEmailAsync(userId, CancellationToken.None);

        var sent = sender.Sent.Should().ContainSingle().Subject;
        var rawToken = TokenIn(sent.Body);
        rawToken.Should().NotBeNullOrEmpty();

        var stored = await db.SecurityTokens.SingleAsync(t => t.UserId == userId && t.Purpose == SecurityTokenPurpose.StaffInvite);
        stored.TokenHash.Should().NotBe(rawToken, "MSP-61: the raw token must never be what's persisted");
        stored.TokenHash.Should().NotContain(rawToken!, "not even as a substring - the stored value must be a genuine hash, not a lightly-obscured copy");
        stored.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
        stored.ConsumedAt.Should().BeNull("unused until accepted");

        var consumed = await tokens.ConsumeAsync(rawToken!, SecurityTokenPurpose.StaffInvite, CancellationToken.None);
        consumed.Should().BeOfType<ConsumeSecurityTokenResult.Success>().Which.UserId.Should().Be(userId);
    }

    private sealed class CapturingSender : IEmailSender
    {
        public List<(Guid UserId, string To, string Subject, string Body)> Sent { get; } = [];
        public Task SendAsync(Guid userId, string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            Sent.Add((userId, toEmail, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    /// <summary>The full real loop: invite -> mint a real token exactly as the email job would ->
    /// accept via the real HTTP endpoint -> log in with the new password through the real login
    /// endpoint -> confirm the JWT carries the invited role's real permissions. No DB flips.</summary>
    [Fact]
    public async Task Accepting_an_invite_lets_the_invited_user_log_in_with_the_assigned_role_s_permissions()
    {
        var admin = await AdminClientAsync();
        var email = $"acceptflow-{Guid.NewGuid():N}@ministry.example";
        var inviteResponse = await admin.PostAsJsonAsync("/api/v1/staff/invite", new { email, fullName = "Accept Flow", role = Roles.OnboardingReviewer });
        var userId = (await inviteResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();

        string rawToken;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<ISecurityTokenService>();
            rawToken = await tokens.IssueAsync(userId, SecurityTokenPurpose.StaffInvite, TimeSpan.FromDays(7), CancellationToken.None);
        }

        var anonymous = fixture.CreateClient();
        const string newPassword = "AcceptFlowPassword#2026!";
        var acceptResponse = await anonymous.PostAsJsonAsync("/api/v1/staff/accept-invite", new { token = rawToken, password = newPassword });
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email, password = newPassword });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, "a real Identity user with a real password was created via UserManager, not a raw DB insert");

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginBody.GetProperty("accessToken").GetString()!;
        var claims = JwtClaims(accessToken);
        claims.Should().Contain(Permissions.SupplierApprove, "onboarding_reviewer's real permission set, proving the role assignment took effect");
        claims.Should().NotContain(Permissions.AdminUsersManage, "onboarding_reviewer must not silently gain system_admin's permissions");
    }

    [Fact]
    public async Task Accepting_with_an_already_used_token_is_rejected()
    {
        var admin = await AdminClientAsync();
        var email = $"reuse-{Guid.NewGuid():N}@ministry.example";
        var inviteResponse = await admin.PostAsJsonAsync("/api/v1/staff/invite", new { email, fullName = "Reuse Check", role = Roles.OnboardingReviewer });
        var userId = (await inviteResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();

        string rawToken;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<ISecurityTokenService>();
            rawToken = await tokens.IssueAsync(userId, SecurityTokenPurpose.StaffInvite, TimeSpan.FromDays(7), CancellationToken.None);
        }

        var client = fixture.CreateClient();
        var first = await client.PostAsJsonAsync("/api/v1/staff/accept-invite", new { token = rawToken, password = "FirstAccept#2026!" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync("/api/v1/staff/accept-invite", new { token = rawToken, password = "SecondAccept#2026!" });

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("invalid_or_expired_token");
    }

    [Fact]
    public async Task Accepting_with_an_expired_token_is_rejected()
    {
        var admin = await AdminClientAsync();
        var email = $"expired-{Guid.NewGuid():N}@ministry.example";
        var inviteResponse = await admin.PostAsJsonAsync("/api/v1/staff/invite", new { email, fullName = "Expired Check", role = Roles.OnboardingReviewer });
        var userId = (await inviteResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();

        string rawToken;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<ISecurityTokenService>();
            // Negative TTL: already expired the instant it's issued - no clock manipulation needed.
            rawToken = await tokens.IssueAsync(userId, SecurityTokenPurpose.StaffInvite, TimeSpan.FromSeconds(-1), CancellationToken.None);
        }

        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/staff/accept-invite", new { token = rawToken, password = "ExpiredAccept#2026!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("invalid_or_expired_token");
    }

    [Fact]
    public async Task Accepting_with_a_garbage_token_is_rejected_not_500()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/staff/accept-invite", new { token = "not-a-real-token", password = "GarbageAccept#2026!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static IReadOnlyList<string> JwtClaims(string accessToken)
    {
        var payload = accessToken.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=').Replace('-', '+').Replace('_', '/');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("perms", out var perms)) return [];
        return perms.ValueKind == JsonValueKind.Array
            ? [.. perms.EnumerateArray().Select(p => p.GetString()!)]
            : [perms.GetString()!];
    }
}
