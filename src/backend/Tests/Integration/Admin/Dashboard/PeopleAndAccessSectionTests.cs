// The people and access section of the administrator's dashboard, every figure read through the real route.
//
// The database is shared by the whole suite and never reset, so no figure here can be asserted as a total. Each
// test reads the section, seeds rows of its own, reads it again and asserts how far each figure moved. And it
// asserts every figure on a side, the ones that must not move included: a seeded deactivated account must leave
// the locked-out count where it was, and a count that wandered would otherwise go unnoticed.
//
// Each test seeds at least one row that the figure must leave out beside the rows it must count, so a figure that
// counted too much moves by more than expected. The rows that look most like the counted ones are the controls: a
// lockout that ran out, a link that expired, a session row that ended, a second role on one person.
//
//
// HOW THE ROWS ARE MADE
//
// Accounts through the identity framework's user manager, so they are real accounts with their roles. Tokens,
// session rows and the staff_invited audit row are added directly, because the figures are about what those rows
// say and the paths that write them have tests of their own. The one exception reads the other way: one test
// invites, accepts and signs in through the real routes, so the invited-never-signed-in figure is shown to match
// the rows the product itself writes, not only the ones this file writes.
//
// That invitation also queues the invitation email. The fixture's own host runs that job whenever it gets to it,
// and the job mints a token of its own each time it tries. So that test mints its own token too before it asserts
// anything about links, and asserts only what holds however many of the job's tokens have appeared by then.
//
// Supplier accounts need a real supplier, so those tests register one first, through the real route, before the
// first read.
//
//
// THE TWO-FACTOR POLICY IS READ FROM CONFIGURATION
//
// The default names only the system administrator. One test runs on a host configured to require it of the
// evaluator instead, spelled in capitals, so a section that named the system administrator itself, or matched the
// configured name with a different rule about letter case than sign-in uses, gets that test wrong.
//
//
// WHAT IS LEFT BEHIND
//
// Every account, token, session row and organisation a test seeds is deleted in its finally. The audit rows stay,
// because the audit table is append-only. So does the account each test signs in with and the supplier it
// registers, like every other class's, each under an address of its own.

namespace MotsSupplierPortal.Tests.Integration.Admin.Dashboard;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Tests.Integration;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class PeopleAndAccessSectionTests(PostgresApiFixture fixture)
{
    private const string Route = "/api/v1/admin/dashboard";

    [Fact]
    public async Task Staff_and_supplier_accounts_are_counted_apart_with_active_and_inactive_apart()
    {
        var admin = await AdminAsync();
        var supplierId = await RegisteredSupplierAsync();
        await using var seed = new Seed(fixture);
        var organisation = await seed.OrganisationAsync(OrganizationType.Hotel, isActive: true);
        var before = await SectionAsync(admin);

        await seed.UserAsync(organizationId: organisation);
        await seed.UserAsync(isActive: false);
        await seed.UserAsync(isActive: false);
        for (var i = 0; i < 3; i++) await seed.UserAsync(supplierId: supplierId);
        for (var i = 0; i < 4; i++) await seed.UserAsync(supplierId: supplierId, isActive: false);

        var after = await SectionAsync(admin);

        Moved(before, after, "staff").Should().BeEquivalentTo(Figures(active: 1, inactive: 2),
            "a member of staff is an account without a company, with a buying body or without one");
        Moved(before, after, "suppliers").Should().BeEquivalentTo(Figures(active: 3, inactive: 4));
        after.EnumerateObject().Select(p => p.Name).Should().NotContain(
            name => name.Contains("total", StringComparison.OrdinalIgnoreCase),
            "staff and supplier accounts are never added together");
    }

    [Fact]
    public async Task Each_role_counts_its_active_holders_and_each_person_is_counted_once()
    {
        var admin = await AdminAsync();
        var supplierId = await RegisteredSupplierAsync();
        await using var seed = new Seed(fixture);
        var before = await SectionAsync(admin);

        await seed.UserAsync(roles: [Roles.Evaluator, Roles.MinistryViewer]);
        await seed.UserAsync(roles: [Roles.Evaluator]);
        await seed.UserAsync(roles: [Roles.Evaluator], isActive: false);
        await seed.UserAsync();
        await seed.UserAsync(supplierId: supplierId, roles: [Roles.SupplierUser]);

        var after = await SectionAsync(admin);

        RoleMoved(before, after, Roles.Evaluator).Should().Be(2, "the deactivated evaluator holds the role and is not counted");
        RoleMoved(before, after, Roles.MinistryViewer).Should().Be(1);
        RoleMoved(before, after, Roles.SupplierUser).Should().Be(1);
        Moved(before, after, "staff").Should().BeEquivalentTo(Figures(active: 3, inactive: 1, activeWithARole: 2),
            "the person holding two roles is one person, and the one holding none is not counted at all");
        Moved(before, after, "suppliers").Should().BeEquivalentTo(Figures(active: 1, activeWithARole: 1));

        after.GetProperty("activeUsersByRole").EnumerateArray().Select(r => r.GetProperty("role").GetString())
            .Should().Contain(Roles.DefaultPermissions.Keys, "every role appears, a role nobody holds included");
    }

    [Fact]
    public async Task Sessions_are_counted_by_sign_in_and_only_while_they_are_alive()
    {
        var admin = await AdminAsync();
        var supplierId = await RegisteredSupplierAsync();
        await using var seed = new Seed(fixture);
        var before = await SectionAsync(admin);

        var a = await seed.UserAsync();
        var oneSignIn = Guid.CreateVersion7();
        await seed.SessionAsync(a, oneSignIn);
        await seed.SessionAsync(a, oneSignIn);
        await seed.SessionAsync(a, Guid.CreateVersion7(), revoked: true);
        await seed.SessionAsync(a, Guid.CreateVersion7(), expired: true);
        var b = await seed.UserAsync();
        await seed.SessionAsync(b, Guid.CreateVersion7());
        var c = await seed.UserAsync(supplierId: supplierId);
        await seed.SessionAsync(c, Guid.CreateVersion7());
        var anotherSignIn = Guid.CreateVersion7();
        await seed.SessionAsync(c, anotherSignIn);
        await seed.SessionAsync(c, anotherSignIn);

        var after = await SectionAsync(admin);

        Moved(before, after, "staff").Should().BeEquivalentTo(
            Figures(active: 2, activeSessions: 2, peopleWithActiveSessions: 2),
            "two live tokens of one sign-in are one session, and an ended or expired sign-in is none");
        Moved(before, after, "suppliers").Should().BeEquivalentTo(
            Figures(active: 1, activeSessions: 2, peopleWithActiveSessions: 1));
    }

    [Fact]
    public async Task An_invitation_is_pending_per_person_while_a_link_works_and_none_has_been_used()
    {
        var admin = await AdminAsync();
        var supplierId = await RegisteredSupplierAsync();
        await using var seed = new Seed(fixture);
        var before = await SectionAsync(admin);

        var twoLinks = await seed.UserAsync();
        await seed.TokenAsync(twoLinks, SecurityTokenPurpose.StaffInvite);
        await seed.TokenAsync(twoLinks, SecurityTokenPurpose.StaffInvite);
        await seed.TokenAsync(await seed.UserAsync(), SecurityTokenPurpose.StaffInvite, expired: true);
        var accepted = await seed.UserAsync();
        await seed.TokenAsync(accepted, SecurityTokenPurpose.StaffInvite, used: true);
        await seed.TokenAsync(accepted, SecurityTokenPurpose.StaffInvite);
        await seed.TokenAsync(await seed.UserAsync(), SecurityTokenPurpose.PasswordReset);
        await seed.TokenAsync(await seed.UserAsync(isActive: false), SecurityTokenPurpose.StaffInvite);

        await seed.TokenAsync(await seed.UserAsync(supplierId: supplierId), SecurityTokenPurpose.SupplierUserInvite);
        var supplierTwoLinks = await seed.UserAsync(supplierId: supplierId);
        await seed.TokenAsync(supplierTwoLinks, SecurityTokenPurpose.SupplierUserInvite);
        await seed.TokenAsync(supplierTwoLinks, SecurityTokenPurpose.SupplierUserInvite);
        await seed.TokenAsync(await seed.UserAsync(supplierId: supplierId), SecurityTokenPurpose.EmailVerification);

        var after = await SectionAsync(admin);

        Moved(before, after, "staff").Should().BeEquivalentTo(Figures(active: 4, inactive: 1, pendingInvitations: 1),
            "only the person with two working links is waiting: the expired link, the accepted invitation, the "
            + "password reset and the deactivated account are not");
        Moved(before, after, "suppliers").Should().BeEquivalentTo(Figures(active: 3, pendingInvitations: 2));
    }

    [Fact]
    public async Task Staff_invited_and_never_signed_in_is_split_by_whether_the_link_still_works()
    {
        var admin = await AdminAsync();
        await using var seed = new Seed(fixture);
        var before = await SectionAsync(admin);

        var waiting = await seed.InvitedAsync();
        await seed.TokenAsync(waiting, SecurityTokenPurpose.StaffInvite);
        var expired = await seed.InvitedAsync();
        await seed.TokenAsync(expired, SecurityTokenPurpose.StaffInvite, expired: true);
        var acceptedOnly = await seed.InvitedAsync();
        await seed.TokenAsync(acceptedOnly, SecurityTokenPurpose.StaffInvite, used: true);
        await seed.InvitedAsync();

        var signedInOnce = await seed.InvitedAsync();
        await seed.SessionAsync(signedInOnce, Guid.CreateVersion7(), revoked: true);
        var neverInvited = await seed.UserAsync();
        await seed.TokenAsync(neverInvited, SecurityTokenPurpose.StaffInvite);
        await seed.InvitedAsync(isActive: false);

        var after = await SectionAsync(admin);

        Moved(before, after, "staffInvitedNeverSignedIn").Should().BeEquivalentTo(
            new Dictionary<string, int> { ["linkStillValid"] = 1, ["linkNoLongerValid"] = 3 },
            "a session row that has ended still means the person signed in once, an account nobody invited is not "
            + "an invitation, and a deactivated one is not counted");
        Moved(before, after, "staff").Should().BeEquivalentTo(
            Figures(active: 6, inactive: 1, activeWithARole: 5, pendingInvitations: 2));
    }

    [Fact]
    public async Task An_invitation_made_through_the_real_routes_is_counted_until_the_person_signs_in()
    {
        var admin = await AdminAsync();
        var email = $"dashboard-invitee-{Guid.NewGuid():N}@ministry.example";
        Guid userId = default;

        try
        {
            var before = await SectionAsync(admin);

            var invited = await admin.PostAsJsonAsync("/api/v1/staff/invite",
                new { email, fullName = "Dashboard Invitee", role = Roles.Evaluator });
            invited.StatusCode.Should().Be(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync());
            userId = (await invited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();

            var whileUnsent = await SectionAsync(admin);
            Moved(before, whileUnsent, "staffInvitedNeverSignedIn").Values.Sum().Should().Be(1,
                "the invitation is recorded against the new account, which has never signed in");

            string rawToken;
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                rawToken = await scope.ServiceProvider.GetRequiredService<ISecurityTokenService>()
                    .IssueAsync(userId, SecurityTokenPurpose.StaffInvite, TimeSpan.FromDays(7), CancellationToken.None);
            }

            var withLink = await SectionAsync(admin);
            Moved(before, withLink, "staffInvitedNeverSignedIn").Should().BeEquivalentTo(
                new Dictionary<string, int> { ["linkStillValid"] = 1, ["linkNoLongerValid"] = 0 });
            Moved(before, withLink, "staff")["pendingInvitations"].Should().Be(1);

            const string password = "DashboardInvitee#2026!";
            var anonymous = fixture.CreateClient();
            (await anonymous.PostAsJsonAsync("/api/v1/staff/accept-invite", new { token = rawToken, password }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var acceptedNotSignedIn = await SectionAsync(admin);
            Moved(before, acceptedNotSignedIn, "staffInvitedNeverSignedIn").Should().BeEquivalentTo(
                new Dictionary<string, int> { ["linkStillValid"] = 0, ["linkNoLongerValid"] = 1 },
                "the link was used, and any the email job minted since are not an open invitation");
            Moved(before, acceptedNotSignedIn, "staff")["pendingInvitations"].Should().Be(0);

            (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email, password }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var signedIn = await SectionAsync(admin);
            Moved(before, signedIn, "staffInvitedNeverSignedIn").Values.Should().OnlyContain(moved => moved == 0);
            Moved(before, signedIn, "staff").Should().BeEquivalentTo(
                Figures(active: 1, activeWithARole: 1, activeSessions: 1, peopleWithActiveSessions: 1));
        }
        finally
        {
            if (userId != default) await Seed.DeleteUsersAsync(fixture, [userId]);
        }
    }

    [Fact]
    public async Task Locked_out_means_a_lockout_that_has_not_run_out()
    {
        var admin = await AdminAsync();
        var supplierId = await RegisteredSupplierAsync();
        await using var seed = new Seed(fixture);
        var before = await SectionAsync(admin);

        await seed.UserAsync(lockoutEnd: DateTimeOffset.UtcNow.AddHours(1));
        await seed.UserAsync(lockoutEnd: DateTimeOffset.UtcNow.AddHours(-1));
        await seed.UserAsync(lockoutEnd: DateTimeOffset.UtcNow.AddHours(1), isActive: false);
        await seed.UserAsync(supplierId: supplierId, lockoutEnd: DateTimeOffset.UtcNow.AddHours(1));

        var after = await SectionAsync(admin);

        Moved(before, after, "staff").Should().BeEquivalentTo(Figures(active: 2, inactive: 1, lockedOut: 1),
            "a lockout that ran out an hour ago is not a lock");
        Moved(before, after, "suppliers").Should().BeEquivalentTo(Figures(active: 1, lockedOut: 1));
    }

    [Fact]
    public async Task Cannot_sign_in_counts_active_accounts_whose_role_needs_a_second_factor_they_have_not_set_up()
    {
        var admin = await AdminAsync();
        await using var seed = new Seed(fixture);
        var before = await SectionAsync(admin);

        await seed.UserAsync(roles: [Roles.SystemAdmin]);
        await seed.UserAsync(roles: [Roles.SystemAdmin], twoFactor: true);
        await seed.UserAsync(roles: [Roles.SystemAdmin], isActive: false);
        await seed.UserAsync(roles: [Roles.Evaluator]);

        var after = await SectionAsync(admin);

        Moved(before, after, "staff").Should().BeEquivalentTo(
            Figures(active: 3, inactive: 1, activeWithARole: 3, cannotSignIn: 1),
            "only the active administrator without a second factor is refused at sign-in");
        after.GetProperty("twoFactorRequiredRoles").EnumerateArray().Select(r => r.GetString())
            .Should().Equal(Roles.SystemAdmin);
    }

    [Fact]
    public async Task Cannot_sign_in_follows_the_configured_policy_whatever_its_letter_case()
    {
        var admin = await AdminAsync();
        await using var seed = new Seed(fixture);
        await using var host = HostRequiringTwoFactorOf("EVALUATOR");
        var client = SignedInOn(host, admin);
        var before = await SectionAsync(client);

        await seed.UserAsync(roles: [Roles.Evaluator]);
        await seed.UserAsync(roles: [Roles.Evaluator], twoFactor: true);
        await seed.UserAsync(roles: [Roles.SystemAdmin]);

        var after = await SectionAsync(client);

        Moved(before, after, "staff").Should().BeEquivalentTo(
            Figures(active: 3, activeWithARole: 3, cannotSignIn: 1),
            "the policy names the evaluator and no longer the administrator, and sign-in matches it in any case");
        after.GetProperty("twoFactorRequiredRoles").EnumerateArray().Select(r => r.GetString())
            .Should().Equal("EVALUATOR");
    }

    [Fact]
    public async Task Organisations_are_counted_by_type_with_active_and_inactive_apart()
    {
        var admin = await AdminAsync();
        await using var seed = new Seed(fixture);
        var before = await SectionAsync(admin);

        await seed.OrganisationAsync(OrganizationType.Hotel, isActive: true);
        await seed.OrganisationAsync(OrganizationType.Hotel, isActive: false);
        await seed.OrganisationAsync(OrganizationType.MotBody, isActive: true);
        await seed.OrganisationAsync(OrganizationType.MotBody, isActive: true);
        await seed.OrganisationAsync(OrganizationType.Ministry, isActive: false);

        var after = await SectionAsync(admin);

        OrganisationsMoved(before, after).Should().BeEquivalentTo(new Dictionary<string, (int, int)>
        {
            [nameof(OrganizationType.Hotel)] = (1, 1),
            [nameof(OrganizationType.MotBody)] = (2, 0),
            [nameof(OrganizationType.Ministry)] = (0, 1),
        }, "every type appears, each with its active and inactive bodies apart");
    }

    [Fact]
    public async Task Placeholder_addresses_are_counted_among_active_supplier_logins_only()
    {
        var admin = await AdminAsync();
        var supplierId = await RegisteredSupplierAsync();
        await using var seed = new Seed(fixture);
        var before = await SectionAsync(admin);

        await seed.UserAsync(supplierId: supplierId, email: $"imported-{Guid.NewGuid():N}@erp-import.invalid");
        await seed.UserAsync(supplierId: supplierId, email: $"IMPORTED-{Guid.NewGuid():N}@ERP-IMPORT.INVALID");
        await seed.UserAsync(supplierId: supplierId, email: $"off-{Guid.NewGuid():N}@erp-import.invalid", isActive: false);
        await seed.UserAsync(supplierId: supplierId, email: $"near-{Guid.NewGuid():N}@erp-import.invalid.example.com");
        await seed.UserAsync(email: $"staff-{Guid.NewGuid():N}@erp-import.invalid");

        var after = await SectionAsync(admin);

        (after.GetProperty("supplierLoginsOnPlaceholderAddresses").GetInt32()
         - before.GetProperty("supplierLoginsOnPlaceholderAddresses").GetInt32())
            .Should().Be(2, "an address that only begins like a placeholder, a deactivated login and a member of "
                            + "staff are not counted, and the domain is matched in any letter case");
    }

    private Task<HttpClient> AdminAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    private async Task<Guid> RegisteredSupplierAsync()
    {
        var (_, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, "Dashboard People Co");
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.Email == email).Select(u => u.SupplierId!.Value).SingleAsync();
    }

    private static async Task<JsonElement> SectionAsync(HttpClient client)
    {
        var response = await client.GetAsync(Route);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);

        var section = JsonDocument.Parse(text).RootElement.GetProperty("peopleAndAccess");
        section.GetProperty("status").GetString().Should().Be("ok", text);
        return section.GetProperty("data").Clone();
    }

    private static Dictionary<string, int> Moved(JsonElement before, JsonElement after, string record) =>
        after.GetProperty(record).EnumerateObject().ToDictionary(
            figure => figure.Name,
            figure => figure.Value.GetInt32() - before.GetProperty(record).GetProperty(figure.Name).GetInt32());

    private static Dictionary<string, int> Figures(
        int active = 0, int inactive = 0, int activeWithARole = 0, int activeSessions = 0,
        int peopleWithActiveSessions = 0, int pendingInvitations = 0, int lockedOut = 0, int cannotSignIn = 0) =>
        new()
        {
            ["active"] = active,
            ["inactive"] = inactive,
            ["activeWithARole"] = activeWithARole,
            ["activeSessions"] = activeSessions,
            ["peopleWithActiveSessions"] = peopleWithActiveSessions,
            ["pendingInvitations"] = pendingInvitations,
            ["lockedOut"] = lockedOut,
            ["cannotSignIn"] = cannotSignIn,
        };

    private static int RoleMoved(JsonElement before, JsonElement after, string role)
    {
        static int Holders(JsonElement data, string role) =>
            data.GetProperty("activeUsersByRole").EnumerateArray()
                .Single(r => r.GetProperty("role").GetString() == role)
                .GetProperty("activeUsers").GetInt32();

        return Holders(after, role) - Holders(before, role);
    }

    private static Dictionary<string, (int Active, int Inactive)> OrganisationsMoved(JsonElement before, JsonElement after)
    {
        static Dictionary<string, (int Active, int Inactive)> ByType(JsonElement data) =>
            data.GetProperty("organisationsByType").EnumerateArray().ToDictionary(
                o => o.GetProperty("type").GetString()!,
                o => (o.GetProperty("active").GetInt32(), o.GetProperty("inactive").GetInt32()));

        var was = ByType(before);
        return ByType(after).ToDictionary(
            o => o.Key,
            o => (o.Value.Active - was[o.Key].Active, o.Value.Inactive - was[o.Key].Inactive));
    }

    private WebApplicationFactory<Program> HostRequiringTwoFactorOf(string role)
    {
        var fixtureKey = fixture.Services.GetRequiredService<JwtSigningKeyProvider>().GetValidationKey();

        return fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mfa:RequiredRoles:0", role);
            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                    options => options.TokenValidationParameters.IssuerSigningKey = fixtureKey));
        });
    }

    private static HttpClient SignedInOn(WebApplicationFactory<Program> host, HttpClient signedIn)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        return client;
    }

    private sealed class Seed(PostgresApiFixture fixture) : IAsyncDisposable
    {
        private readonly List<Guid> _users = [];
        private readonly List<Guid> _organisations = [];

        public async Task<Guid> UserAsync(
            Guid? supplierId = null, Guid? organizationId = null, string[]? roles = null, bool isActive = true,
            bool twoFactor = false, DateTimeOffset? lockoutEnd = null, string? email = null)
        {
            email ??= $"dashboard-person-{Guid.NewGuid():N}@{(supplierId is null ? "ministry" : "supplier")}.example";
            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                FullName = "Dashboard Person",
                EmailConfirmed = true,
                IsActive = isActive,
                SupplierId = supplierId,
                OrganizationId = organizationId,
                TwoFactorEnabled = twoFactor,
                LockoutEnabled = true,
                LockoutEnd = lockoutEnd,
            };

            await using var scope = fixture.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var created = await userManager.CreateAsync(user);
            created.Succeeded.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Description)));
            _users.Add(user.Id);

            if (roles is { Length: > 0 })
            {
                (await userManager.AddToRolesAsync(user, roles)).Succeeded.Should().BeTrue();
            }

            return user.Id;
        }

        public async Task<Guid> InvitedAsync(bool isActive = true)
        {
            var userId = await UserAsync(roles: [Roles.Evaluator], isActive: isActive);
            await WithDbAsync(db => db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.CreateVersion7(),
                OccurredAt = DateTimeOffset.UtcNow,
                ActorKind = AuditActorKind.User,
                AggregateType = "AppUser",
                AggregateId = userId,
                Action = "staff_invited",
                ToState = Roles.Evaluator,
                CorrelationId = Guid.CreateVersion7(),
            }));
            return userId;
        }

        public Task TokenAsync(Guid userId, SecurityTokenPurpose purpose, bool expired = false, bool used = false) =>
            WithDbAsync(db => db.SecurityTokens.Add(new SecurityToken
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                TokenHash = Guid.NewGuid().ToString("N"),
                Purpose = purpose,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresAt = expired ? DateTimeOffset.UtcNow.AddMinutes(-5) : DateTimeOffset.UtcNow.AddDays(6),
                ConsumedAt = used ? DateTimeOffset.UtcNow.AddHours(-1) : null,
            }));

        public Task SessionAsync(Guid userId, Guid familyId, bool revoked = false, bool expired = false) =>
            WithDbAsync(db => db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                TokenHash = Guid.NewGuid().ToString("N"),
                FamilyId = familyId,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresAt = expired ? DateTimeOffset.UtcNow.AddMinutes(-5) : DateTimeOffset.UtcNow.AddDays(29),
                RevokedAt = revoked ? DateTimeOffset.UtcNow.AddHours(-1) : null,
            }));

        public async Task<Guid> OrganisationAsync(OrganizationType type, bool isActive)
        {
            Guid id = default;
            await WithDbAsync(async db =>
            {
                var organisation = Organization.Create(
                    await ReferenceCodeGenerator.NextCodeAsync(db, "ORG", CancellationToken.None),
                    "جهة لوحة المتابعة", $"Dashboard Body {Guid.NewGuid():N}", type);
                db.Organizations.Add(organisation);
                db.Entry(organisation).Property(o => o.IsActive).CurrentValue = isActive;
                id = organisation.Id;
            });
            _organisations.Add(id);
            return id;
        }

        public async ValueTask DisposeAsync()
        {
            await DeleteUsersAsync(fixture, _users);
            await WithDbAsync(async db =>
            {
                await db.Organizations.Where(o => _organisations.Contains(o.Id)).ExecuteDeleteAsync();
            });
        }

        public static async Task DeleteUsersAsync(PostgresApiFixture fixture, IReadOnlyCollection<Guid> userIds)
        {
            Guid[] ids = [.. userIds];
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
            await db.SecurityTokens.Where(t => ids.Contains(t.UserId)).ExecuteDeleteAsync();
            await db.RefreshTokens.Where(t => ids.Contains(t.UserId)).ExecuteDeleteAsync();
        }

        private Task WithDbAsync(Action<AppDbContext> change) =>
            WithDbAsync(db =>
            {
                change(db);
                return Task.CompletedTask;
            });

        private async Task WithDbAsync(Func<AppDbContext, Task> change)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await change(db);
            await db.SaveChangesAsync();
        }
    }
}
