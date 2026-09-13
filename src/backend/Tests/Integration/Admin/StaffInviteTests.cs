// The in-product staff invitation flow, through the real contract.
//
//
// TWO THINGS THIS DELIBERATELY DOES NOT DO
//
// It never waits on the background worker to process an enqueued job, because the test host does not run one and a
// live development worker happening to be up would make the suite flaky and non-hermetic.
//
// And it never asserts a token was in an email body, because the job deliberately mints the token AT SEND TIME. So
// testing the hashed-not-plaintext property means invoking the job directly, which is the convention the email
// behaviour suite already follows.
//
// That test also seeds its user directly rather than through the real invitation endpoint, and that is empirical: the
// host registers a real background server unconditionally, so it actually runs one in-process against the same
// database, and a real invitation call gets its job picked up and processed independently of the test's own direct
// call, minting a SECOND token row for the same user and purpose. The test failed on finding two until it was
// switched to seeding directly. The email behaviour suite does not hit this because it asserts against its own
// captured list rather than a row count, so the same race exists there too, just invisible to what it checks.
//
//
// THE ACCOUNT IS UNUSABLE UNTIL THE INVITATION IS ACCEPTED
//
// It exists but no password the caller could guess or was ever told will work. Only acceptance, gated by the token,
// can set a real one.
//
// The full loop is asserted with no database writes: invite, mint a real token exactly as the job would, accept
// through the real endpoint, sign in through the real endpoint, and confirm the token carries the invited role's
// real permissions. Expiry is tested with a negative lifetime, which is already expired the instant it is issued, so
// no clock manipulation is needed.
//
//
// WHO MAY INVITE, AND WHICH ROLES MAY BE INVITED
//
// A reviewer has real permissions but not this one, which proves it is a genuine authorisation check rather than
// "any authenticated staff member".
//
// The supplier roles are refused, because those accounts come from registration or the supplier's own team
// invitation and an account made here never gets a company, so granting one would be a mismatch by construction.
//
//
// A PROCUREMENT INVITATION MUST NAME A BUYING BODY
//
// The organization was optional for every role and the validator checked only the address, the name and the role.
//
// Every procurement query is scoped by the caller's organization, so an officer invited without one signed in
// successfully, held every permission the role grants, and met an empty product: no tenders, no dashboard figures,
// no approval queue, and no error anywhere, because returning nothing is the CORRECT answer to "show me the tenders
// of no organization". The account looked fine and could do nothing.
//
// The control is the half that makes the rule narrow rather than blunt. Five roles deliberately belong to no buying
// body: an evaluator is scoped by ASSIGNMENT and may have none, a reviewer works the national registry, the ministry
// viewer's grant is cross-organization and pinning it to one would narrow it, and an administrator has no tenancy.
// Requiring one of any of them would be refusing a legitimate invitation.
//
// The duplicate-address test uses a role that needs no organization, deliberately: it is about the duplicate, and a
// procurement role with none is now refused at validation, which would fail before the duplicate check ever ran and
// pass for the wrong reason.

namespace MotsSupplierPortal.Tests.Integration.Admin;

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
using MotsSupplierPortal.Tests.Integration;

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

        (await userManager.CheckPasswordAsync(user, "whatever-a-caller-might-try")).Should().BeFalse();
    }

    [Fact]
    public async Task Invite_rejects_a_caller_without_admin_users_manage()
    {
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
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/staff/invite", new
        {
            email = $"wrongrole-{Guid.NewGuid():N}@ministry.example",
            fullName = "Wrong Role",
            role = Roles.SupplierAdmin,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("INVALID_ROLE");
    }

    [Fact]
    public async Task Invite_refuses_a_duplicate_email()
    {
        var admin = await AdminClientAsync();
        var email = $"dup-{Guid.NewGuid():N}@ministry.example";
        await admin.PostAsJsonAsync("/api/v1/staff/invite", new { email, fullName = "First", role = Roles.OnboardingReviewer });

        var second = await admin.PostAsJsonAsync("/api/v1/staff/invite", new { email, fullName = "Second", role = Roles.OnboardingReviewer });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

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
        var jobs = new EmailJobs(sender, db, tokens, scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            scope.ServiceProvider.GetRequiredService<MotsSupplierPortal.Application.Admin.IEmailCopySource>());

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
        body.GetProperty("code").GetString().Should().Be("INVALID_OR_EXPIRED_TOKEN");
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
            rawToken = await tokens.IssueAsync(userId, SecurityTokenPurpose.StaffInvite, TimeSpan.FromSeconds(-1), CancellationToken.None);
        }

        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/staff/accept-invite", new { token = rawToken, password = "ExpiredAccept#2026!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("INVALID_OR_EXPIRED_TOKEN");
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

    [Theory]
    [InlineData(Roles.ProcurementOfficer)]
    [InlineData(Roles.ProcurementManager)]
    public async Task Invite_refuses_a_procurement_role_with_no_organization(string role)
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/staff/invite", new
        {
            email = $"noorg-{Guid.NewGuid():N}@mots.local",
            fullName = "No Organization",
            role,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("organizationId", "the refusal must name the field that is missing");
    }

    [Theory]
    [InlineData(Roles.OnboardingReviewer)]
    [InlineData(Roles.Evaluator)]
    [InlineData(Roles.MinistryViewer)]
    public async Task Invite_still_accepts_a_role_that_belongs_to_no_buying_body(string role)
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/staff/invite", new
        {
            email = $"noorg-ok-{Guid.NewGuid():N}@mots.local",
            fullName = "Rightly Without One",
            role,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Invite_accepts_a_procurement_role_that_names_its_organization()
    {
        var admin = await AdminClientAsync();
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);

        var response = await admin.PostAsJsonAsync("/api/v1/staff/invite", new
        {
            email = $"withorg-{Guid.NewGuid():N}@mots.local",
            fullName = "With Organization",
            role = Roles.ProcurementOfficer,
            organizationId = org.Id,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
