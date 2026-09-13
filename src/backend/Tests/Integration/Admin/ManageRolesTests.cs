// Listing roles and editing a role's permission set, with the two guards that make it safe.
//
// An unrecognised permission string is refused, and so is an edit that would leave no role able to ever edit roles
// again.
//
// Every change is audited. And the permission resolver reads a role's permissions from the stored claims rather than
// the shipped map, specifically so an edit reaches the next sign-in: the last test here is the proof of that rather
// than an assumption, granting a role a permission it did not have, signing a fresh user in, and reading the token.
//
//
// THE RESTORE IS A SCOPE, AND THAT FIXES TWO FAULTS AT ONCE
//
// The restores in this file were bare statements at the end of each test, so a failing assertion above them skipped
// the restore entirely, and they put back a set written from memory rather than the one that was there.
//
// One of them restored a single permission while the seeder grants that role two, so the test that exists to prove
// role editing works was itself quietly editing a role for the rest of the run.
//
// Reading the set first and writing it back is the only version that cannot drift: it does not need to know what the
// seed grants, and it stays right when the seed changes.
//
// The update REPLACES a role's whole set rather than merging, so a snapshot has to be taken before the write or the
// restore only puts back the one permission the test happened to be about. That exact leak silently broke every later
// tender test in this shared database when it was missing.
//
//
// AND THE RESTORE HAS TO USE A FRESH SCOPE, WHICH IT DID NOT
//
// The identity framework stamps every role row with a concurrency value and advances it on each write. Instances
// captured before the change, and the ones the same context is still tracking, carry the old stamp, so adding a claim
// returns a FAILED result rather than throwing.
//
// Nothing checked that result, so the restore reported success and wrote nothing: two roles had been losing a
// permission to this test ever since, and the shared-row check is what finally said so.
//
// Re-fetching inside the old scope returns the same tracked, stale entity and fails the same way.
//
//
// THE TWO REGRESSION CASES, EACH FROM A REAL DEFECT
//
// The permission checklist used to be derived from the union of what roles already hold rather than from the
// canonical catalogue, so a permission not yet granted to ANY role was invisible on the screen and could only be
// granted by writing to the database.
//
// That is reproduced by stripping a real permission from every role that holds it, which is exactly the state right
// after a new permission is added and before anybody has granted it, and then granting it back through the real
// endpoint with no database workaround.
//
// And the seeder's pre-existing-role path is never exercised by a fresh database, where every role looks newly
// created. A role created before claim seeding existed has no claims and no marker, and re-running the seeder must
// backfill its defaults rather than leave every user of that role with an empty permission claim, which is exactly
// what shipped locally before this test was added. Re-running again must not duplicate anything or reset an
// administrator's later edit.
//
// That test manipulates a role other classes in this collection depend on, so its restore is unconditional.

namespace MotsSupplierPortal.Tests.Integration.Admin;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ManageRolesTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminClientAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    private static async Task<IAsyncDisposable> PreserveRolePermissionsAsync(HttpClient admin, string role)
    {
        var current = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/roles");
        var permissions = current.GetProperty("roles").EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == role)
            .GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()!)
            .ToArray();

        return new RolePermissions(admin, role, permissions);
    }

    private sealed class RolePermissions(HttpClient admin, string role, string[] permissions) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            var restored = await admin.PutAsJsonAsync($"/api/v1/admin/roles/{role}/permissions", new { permissions });
            restored.StatusCode.Should().Be(HttpStatusCode.OK,
                "a role this test edited must be put back, or every test after it runs against the edit");
        }
    }

    [Fact]
    public async Task List_returns_every_seeded_role_with_its_current_permissions()
    {
        var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/v1/admin/roles");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var roles = body.GetProperty("roles");
        var names = roles.EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();
        names.Should().Contain(Roles.SystemAdmin).And.Contain(Roles.OnboardingReviewer).And.Contain(Roles.SupplierUser);

        var reviewer = roles.EnumerateArray().Single(r => r.GetProperty("name").GetString() == Roles.OnboardingReviewer);
        reviewer.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().Contain(Permissions.SupplierApprove);
    }

    [Fact]
    public async Task Listing_roles_includes_a_permission_not_yet_granted_to_any_role()
    {
        Permissions.All.Should().Contain(Permissions.OfferingSearch,
            "this test's premise is that the permission IS in the canonical catalog");

        await using var scope = fixture.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var allRoles = await roleManager.Roles.ToListAsync();
        var strippedFrom = new List<IdentityRole<Guid>>();

        var officerRole = allRoles.Single(r => r.Name == Roles.ProcurementOfficer);
        var originalOfficerPermissions = (await roleManager.GetClaimsAsync(officerRole))
            .Where(c => c.Type == "perms").Select(c => c.Value).ToList();

        try
        {
            foreach (var role in allRoles)
            {
                var claims = await roleManager.GetClaimsAsync(role);
                var match = claims.FirstOrDefault(c => c.Type == "perms" && c.Value == Permissions.OfferingSearch);
                if (match is null) continue;
                await roleManager.RemoveClaimAsync(role, match);
                strippedFrom.Add(role);
            }
            strippedFrom.Should().NotBeEmpty("the seed must have granted offering.search somewhere for this test's premise to hold");

            var admin = await AdminClientAsync();
            var listResponse = await admin.GetAsync("/api/v1/admin/roles");
            var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();

            listBody.GetProperty("allPermissions").EnumerateArray().Select(p => p.GetString())
                .Should().Contain(Permissions.OfferingSearch,
                    "the catalog must list a permission no role currently holds, or it can never be granted through this UI");

            var systemAdmin = listBody.GetProperty("roles").EnumerateArray().Single(r => r.GetProperty("name").GetString() == Roles.SystemAdmin);
            systemAdmin.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
                .Should().NotContain(Permissions.OfferingSearch, "confirms the strip above actually took effect - not a false positive");

            var requestedPermissions = originalOfficerPermissions
                .Where(p => p != Permissions.OfferingSearch)
                .Append(Permissions.OfferingSearch)
                .ToArray();
            var grant = await admin.PutAsJsonAsync($"/api/v1/admin/roles/{Roles.ProcurementOfficer}/permissions",
                new { permissions = requestedPermissions });
            grant.StatusCode.Should().Be(HttpStatusCode.OK);
            var grantBody = await grant.Content.ReadFromJsonAsync<JsonElement>();
            grantBody.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
                .Should().Contain(Permissions.OfferingSearch);
        }
        finally
        {
            foreach (var claim in (await roleManager.GetClaimsAsync(officerRole)).Where(c => c.Type == "perms"))
            {
                await roleManager.RemoveClaimAsync(officerRole, claim);
            }
            foreach (var permission in originalOfficerPermissions)
            {
                await roleManager.AddClaimAsync(officerRole, new System.Security.Claims.Claim("perms", permission));
            }

            await using var restoreScope = fixture.Services.CreateAsyncScope();
            var restoreRoleManager = restoreScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            foreach (var stale in strippedFrom.Where(r => r.Id != officerRole.Id))
            {
                var role = await restoreRoleManager.FindByIdAsync(stale.Id.ToString());
                if (role is null) continue;

                var current = await restoreRoleManager.GetClaimsAsync(role);
                if (current.Any(c => c.Type == "perms" && c.Value == Permissions.OfferingSearch)) continue;

                var restored = await restoreRoleManager.AddClaimAsync(role, new System.Security.Claims.Claim("perms", Permissions.OfferingSearch));
                restored.Succeeded.Should().BeTrue(
                    $"offering.search must go back on {role.Name}, or every later test runs against a role this one edited. "
                    + string.Join("; ", restored.Errors.Select(e => $"{e.Code}: {e.Description}")));
            }
        }
    }

    [Fact]
    public async Task Non_admin_caller_is_forbidden()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var response = await reviewer.GetAsync("/api/v1/admin/roles");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Updating_with_an_unrecognized_permission_is_rejected()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync($"/api/v1/admin/roles/{Roles.Evaluator}/permissions",
            new { permissions = new[] { "not.a.real.permission" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("INVALID_PERMISSION");
    }

    [Fact]
    public async Task Removing_the_only_role_holding_AdminRolesManage_is_rejected()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync($"/api/v1/admin/roles/{Roles.SystemAdmin}/permissions",
            new { permissions = new[] { Permissions.AdminUsersManage } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("WOULD_LOCK_OUT_ROLE_MANAGEMENT");

        var after = await admin.GetAsync("/api/v1/admin/roles");
        var afterBody = await after.Content.ReadFromJsonAsync<JsonElement>();
        var systemAdmin = afterBody.GetProperty("roles").EnumerateArray().Single(r => r.GetProperty("name").GetString() == Roles.SystemAdmin);
        systemAdmin.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().Contain(Permissions.AdminRolesManage);
    }

    [Fact]
    public async Task A_valid_update_persists_and_is_audited()
    {
        var admin = await AdminClientAsync();
        await using var preserved = await PreserveRolePermissionsAsync(admin, Roles.MinistryViewer);

        var response = await admin.PutAsJsonAsync($"/api/v1/admin/roles/{Roles.MinistryViewer}/permissions",
            new { permissions = new[] { Permissions.AuditRead } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().BeEquivalentTo([Permissions.AuditRead]);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
        var auditRow = await db.AuditLogs
            .Where(a => a.AggregateType == "Role" && a.Action == "role_permissions_updated" && a.ToState == Roles.MinistryViewer)
            .OrderByDescending(a => a.OccurredAt)
            .FirstOrDefaultAsync();
        auditRow.Should().NotBeNull("every role-permission change must be audited");
        auditRow!.Changes.Should().NotBeNull().And.Contain("permissions");

    }

    [Fact]
    public async Task A_role_permission_change_reaches_the_next_login_s_JWT()
    {
        var admin = await AdminClientAsync();
        await using var preserved = await PreserveRolePermissionsAsync(admin, Roles.Evaluator);
        var email = $"jwtcheck-{Guid.NewGuid():N}@ministry.example";

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser { Id = Guid.CreateVersion7(), UserName = email, Email = email, FullName = "JWT Check", EmailConfirmed = true, IsActive = true };
            (await userManager.CreateAsync(user, StaffTestClient.Password)).Succeeded.Should().BeTrue();
            (await userManager.AddToRoleAsync(user, Roles.Evaluator)).Succeeded.Should().BeTrue();
        }

        var beforeLogin = fixture.CreateClient();
        var beforeResponse = await beforeLogin.PostAsJsonAsync("/api/v1/auth/login", new { email, password = StaffTestClient.Password });
        beforeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var beforeToken = (await beforeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        JwtClaims(beforeToken).Should().NotContain(Permissions.AuditRead, "evaluator does not hold audit.read by default");

        var update = await admin.PutAsJsonAsync($"/api/v1/admin/roles/{Roles.Evaluator}/permissions",
            new { permissions = new[] { Permissions.EvaluationScore, Permissions.AuditRead } });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterLogin = fixture.CreateClient();
        var afterResponse = await afterLogin.PostAsJsonAsync("/api/v1/auth/login", new { email, password = StaffTestClient.Password });
        afterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterToken = (await afterResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        JwtClaims(afterToken).Should().Contain(Permissions.AuditRead,
            "the role edit must reach a fresh login's JWT, proving PermissionResolver reads live DB claims, not the static seed dictionary");

    }

    [Fact]
    public async Task Reseeding_a_role_that_predates_claim_seeding_backfills_its_default_permissions()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        var systemAdminRole = await roleManager.FindByNameAsync(Roles.SystemAdmin);
        systemAdminRole.Should().NotBeNull();
        var originalClaims = await roleManager.GetClaimsAsync(systemAdminRole!);

        try
        {
            foreach (var claim in originalClaims)
            {
                await roleManager.RemoveClaimAsync(systemAdminRole!, claim);
            }
            (await roleManager.GetClaimsAsync(systemAdminRole!)).Should().BeEmpty("claims stripped to simulate a pre-migration state");

            await RoleSeeder.SeedAsync(roleManager);

            var afterClaims = await roleManager.GetClaimsAsync(systemAdminRole!);
            afterClaims.Where(c => c.Type == "perms").Select(c => c.Value)
                .Should().BeEquivalentTo(Roles.DefaultPermissions[Roles.SystemAdmin],
                    "a role that existed before claim-seeding must be backfilled on the next startup, not left with zero permissions");

            await roleManager.RemoveClaimAsync(systemAdminRole!, new System.Security.Claims.Claim("perms", Permissions.AuditRead));
            await RoleSeeder.SeedAsync(roleManager);
            var afterSecondRun = await roleManager.GetClaimsAsync(systemAdminRole!);
            afterSecondRun.Where(c => c.Type == "perms" && c.Value == Permissions.AuditRead).Should().BeEmpty(
                "once seeded, the marker claim must stop SeedAsync from ever re-adding a permission an admin deliberately removed");
        }
        finally
        {
            foreach (var claim in await roleManager.GetClaimsAsync(systemAdminRole!))
            {
                await roleManager.RemoveClaimAsync(systemAdminRole!, claim);
            }
            foreach (var claim in originalClaims)
            {
                await roleManager.AddClaimAsync(systemAdminRole!, claim);
            }
        }
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
