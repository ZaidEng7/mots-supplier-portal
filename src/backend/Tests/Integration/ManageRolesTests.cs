using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>FR-ADM-002: system_admin lists roles and edits a role's permission set, with an
/// InvalidPermission guard (unrecognized permission string) and a WouldLockOutRoleManagement
/// guard (an edit that would leave zero roles able to ever edit roles again). Every change is
/// audited, and PermissionResolver reads role permissions from DB claims (not the static
/// Roles.DefaultPermissions dictionary) specifically so an edit reaches the next login - the
/// last test in this file is the proof of that, not an assumption.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ManageRolesTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminClientAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    /// <summary>
    /// Holds a role's CURRENT permission set and puts exactly that back when the scope ends.
    ///
    /// <para><b>T-073, and it fixes two faults at once.</b> The restores in this file were bare
    /// statements at the end of a test - a failing assertion above them skipped the restore
    /// entirely - and they restored a set written from memory rather than the one that was there.
    /// <c>ministry_viewer</c>'s restore put back <c>governance.read</c> alone, while the seeder
    /// grants it <c>report.read</c> as well, so the test that exists to prove role editing works was
    /// itself quietly editing a role for the rest of the run.</para>
    ///
    /// <para>Reading the set first and writing it back is the only version that cannot drift: it
    /// does not need to know what the seed grants, and it stays right when the seed changes.</para>
    /// </summary>
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

    /// <summary>Regression test for a real bug: the roles admin UI used to derive its permission
    /// checklist from the union of what roles already hold, not the canonical Permissions.All
    /// catalog - so a permission not yet granted to ANY role (including system_admin, which by
    /// seed holds everything) was invisible in the UI and could only ever be granted via a direct
    /// DB write. Reuses offering.search as the real example: strips it from every role's claims
    /// (simulating the exact state right after a new permission is added to the catalog, before
    /// anyone has granted it anywhere - system_admin included), then proves allPermissions still
    /// lists it and it can be granted through the real update endpoint with no DB workaround.</summary>
    [Fact]
    public async Task Listing_roles_includes_a_permission_not_yet_granted_to_any_role()
    {
        Permissions.All.Should().Contain(Permissions.OfferingSearch,
            "this test's premise is that the permission IS in the canonical catalog");

        // Strip offering.search from every role holding it (procurement_officer, procurement_manager,
        // and system_admin via Permissions.All) to simulate the exact state right after a new
        // permission is added to the catalog, before anyone has granted it anywhere. Restored in
        // finally so this never leaks into another test in the shared collection database, same
        // discipline as Reseeding_a_role_that_predates_claim_seeding_backfills_its_default_permissions
        // above.
        await using var scope = fixture.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var allRoles = await roleManager.Roles.ToListAsync();
        var strippedFrom = new List<IdentityRole<Guid>>();

        // UpdateRolePermissionsHandler REPLACES a role's entire permission set (remove-all then
        // add-requested), not merges - so the PUT below wipes out every OTHER permission
        // procurement_officer holds (rfq.create/rfq.edit/... as of EPIC-07), not just
        // offering.search. Snapshotting the full set here, before the PUT, is what makes the
        // finally block able to genuinely restore it, rather than only restoring the one
        // permission this test happened to be about - the exact class of leak that silently broke
        // every later RFQ integration test in this shared-DB collection when it was missing.
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

            // The real proof: grant it back through the actual endpoint, no DB write. Includes the
            // rest of procurement_officer's real permission set (minus offering.search, stripped
            // above) rather than an arbitrary two-item list, since this PUT is a full replacement.
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
            // Restore procurement_officer's exact original permission set (the PUT above replaced
            // it wholesale) before restoring offering.search on every other stripped role.
            foreach (var claim in (await roleManager.GetClaimsAsync(officerRole)).Where(c => c.Type == "perms"))
            {
                await roleManager.RemoveClaimAsync(officerRole, claim);
            }
            foreach (var permission in originalOfficerPermissions)
            {
                await roleManager.AddClaimAsync(officerRole, new System.Security.Claims.Claim("perms", permission));
            }

            // T-073. This restore never worked, and said nothing about it.
            //
            // Identity stamps every role row with a ConcurrencyStamp and bumps it on each write. The
            // instances captured before the strip - and the ones this scope's DbContext is still
            // tracking - carry the stamp from before the PUTs above, so AddClaimAsync returns a
            // FAILED IdentityResult ("Optimistic concurrency failure") rather than throwing. Nothing
            // checked the result, so the restore reported success and wrote nothing:
            // procurement_manager and system_admin have been losing offering.search to this test
            // ever since, and the global-row check is what finally said so.
            //
            // A FRESH scope is the fix. Re-fetching inside the old one returns the tracked, stale
            // entity and fails the same way.
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

        // system_admin is (by seed) the only role holding admin.roles.manage. Stripping it here
        // would mean no caller could ever edit a role's permissions again.
        var response = await admin.PutAsJsonAsync($"/api/v1/admin/roles/{Roles.SystemAdmin}/permissions",
            new { permissions = new[] { Permissions.AdminUsersManage } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("WOULD_LOCK_OUT_ROLE_MANAGEMENT");

        // And nothing was actually changed.
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

        // The restore is the `await using` at the top of this test now.
        //
        // It used to be these three lines, and they were wrong twice over: a failing assertion above
        // skipped them, and they put back governance.read ALONE while the seeder also grants
        // ministry_viewer report.read - so the fix for one leak (EPIC-18's, which made the governance
        // suite pass alone and fail in a full run) quietly introduced another.
    }

    /// <summary>The real proof this feature works end-to-end, not just that the DB row changed:
    /// grant a role a permission it did not have, log a fresh user in with that role, and confirm
    /// the JWT's "perms" claims actually reflect the edit.</summary>
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

        // Restored by the `await using` at the top. This used to be a bare statement here, reading
        // the set from Roles.DefaultPermissions - right today, and wrong the moment a migration or an
        // administrator grants the evaluator something the static dictionary does not list.
    }

    /// <summary>Regression test for a real bug caught in manual verification, not by the rest of
    /// this suite: every other test here runs against a brand-new Testcontainers database, so
    /// RoleSeeder always sees roles as newly created and never exercises the pre-existing-role
    /// path. A role created by an OLDER version of RoleSeeder (before role-claim seeding existed
    /// at all) has no claims and no "perms:seeded" marker - re-running SeedAsync against it must
    /// backfill the default permissions, not leave every user of that role with an empty JWT
    /// "perms" claim (which is exactly what shipped locally before this test was added).</summary>
    [Fact]
    public async Task Reseeding_a_role_that_predates_claim_seeding_backfills_its_default_permissions()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // RoleSeeder only iterates Roles.DefaultPermissions' own role names, so simulate the
        // pre-migration state on a real seeded role (system_admin) by stripping its claims - this
        // is exactly what a database created before role-claim seeding existed would look like.
        // system_admin is shared, live state other test classes in this same collection depend on
        // (e.g. AuditSearchAndExportTests logs in as system_admin and expects audit.read) - a
        // try/finally restores its exact original claims unconditionally, so this test's own
        // manipulation never leaks into any test that happens to run after it.
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

            // And re-running again must NOT re-add duplicates or reset an admin's subsequent edit.
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
