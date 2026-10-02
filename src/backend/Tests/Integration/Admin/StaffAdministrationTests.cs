// T-077 with SCR-701 and SCR-702, both P0 and both with no endpoint at all before this: `system_admin`
// could invite a staff account and then never list, deactivate, re-role or MFA-reset one. An account
// created in error could not be removed, which is the half of this that is a security gap.
//
// The invite helper names a buying body where the role requires one. A procurement officer or manager is
// refused without an organisation, because BRULE-029 scopes every one of their queries to it and an
// invitation without one produces an account that signs in and meets an empty product. These tests are
// about role administration rather than about that rule, so the helper satisfies it instead of working
// around it - passing a role that needs no organisation would have changed what the tests are about.
//
// The list carries the facts an administrator needs, and a supplier's user must NOT be in it: a supplier
// administers their own team under SCR-160, and mixing the two would put a supplier's staff in the
// platform list. It is paged through rather than read off the first page - the list is keyset-ordered by
// email and the suite creates many staff accounts, so "it is on page one" is an order dependence, and it
// failed exactly that way in a full run. Following the cursor also exercises the paging. The supplier's
// absence is asserted by predicate over the whole page rather than by counting, because the suite's other
// tests contribute rows too.
//
// The active-session figure counts sign-ins that are still alive, not token rows. It once counted every
// unrevoked row, and the development data showed seventy-six of them standing for nine sign-ins. The account
// is seeded with one sign-in holding two live tokens, a second sign-in holding one, a sign-in whose only token
// expired without being revoked, which is what an old sign-in leaves behind, and one whose only token was
// revoked. Each dead token sits in a sign-in of its own, so counting it by any route moves the answer off two,
// and so does counting tokens rather than sign-ins. The figure is read from the list and from a staff change's
// read-back, the two places an administrator sees it. The change is re-assigning the role the account already
// holds, because it revokes nothing and so leaves the seeded sessions there to be counted. The seeded tokens
// are removed afterwards, so nothing else that reads the session table meets them.
//
// Deactivation is set up with a live session, so "kills its sessions" is measurable rather than vacuous,
// and its control is that this is deactivation and not deletion: the row is still there and can come back.
// The account is the actor on audit rows, and an audit trail pointing at a row that no longer exists is
// not an audit trail - D-28's reasoning, more strongly here.
//
// A role change replaces the role rather than accumulating one: two roles would give an account
// permissions the list cannot show. A role a staff account may not hold is refused, because a supplier
// role on an account with no SupplierId is a broken account - InviteStaffHandler's own reasoning, from the
// other side.
//
// A role change cannot go around the invitation's organisation rule. An evaluator invited without an
// organisation, which that role allows, is refused the officer's and the manager's role with the same
// field-level failure the invitation gives, and keeps the role it had, with no change on the trail. Such an
// account would meet an empty product, and with no organisation it would also look like the platform
// administrator to the award retry and the status banner. The control is an evaluator invited WITH an
// organisation, made a manager by the same request, so the refusal is about the missing organisation rather
// than about leaving the evaluator's role.
//
// Acting on your own account is refused for deactivation, for a demotion out of system_admin and for an
// MFA reset, because each one would leave the actor outside the surface that could undo it. The control
// proves those three are about SELF rather than about system_admin: another administrator can be
// deactivated, because one remains.
//
// Permission is checked with its own control. A supplier's user answers 404 rather than 403 per §9.2's
// row-scoping answer: there is nothing in the difference between "not a staff account" and "no such user"
// that an administrator needs and an attacker does not.

namespace MotsSupplierPortal.Tests.Integration.Admin;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class StaffAdministrationTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    private static async Task<Guid> InviteAsync(HttpClient admin, string role, Guid? organizationId = null)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/staff/invite", new
        {
            email = $"staff-{Guid.NewGuid():N}@example.com",
            fullName = "Invited Staffer",
            role,
            organizationId,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();
    }

    private static async Task<List<JsonElement>> ListEveryStaffAccountAsync(HttpClient admin)
    {
        var rows = new List<JsonElement>();
        string? cursor = null;
        for (var page = 0; page < 20; page++)
        {
            var url = cursor is null ? "/api/v1/staff?withCount=true" : $"/api/v1/staff?cursor={Uri.EscapeDataString(cursor)}";
            var body = await admin.GetFromJsonAsync<JsonElement>(url);
            rows.AddRange(body.GetProperty("data").EnumerateArray());

            var pagination = body.GetProperty("pagination");
            if (!pagination.GetProperty("hasMore").GetBoolean()) break;
            cursor = pagination.GetProperty("nextCursor").GetString();
            if (cursor is null) break;
        }

        return rows;
    }

    [Fact]
    public async Task The_list_carries_the_facts_an_administrator_needs_and_no_supplier_users()
    {
        var admin = await AdminAsync();
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var invitedId = await InviteAsync(admin, Roles.ProcurementOfficer, org.Id);

        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Staff List Outsider Co");

        var rows = await ListEveryStaffAccountAsync(admin);

        var invited = rows.Single(r => r.GetProperty("userId").GetGuid() == invitedId);
        invited.GetProperty("role").GetString().Should().Be(Roles.ProcurementOfficer);
        invited.GetProperty("isActive").GetBoolean().Should().BeTrue();
        invited.GetProperty("mfaEnabled").GetBoolean().Should().BeFalse("a freshly invited account has not enrolled");
        invited.GetProperty("activeSessionCount").GetInt32().Should().Be(0, "it has never signed in");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplierUserIds = await db.Users.Where(u => u.SupplierId != null).Select(u => u.Id).ToListAsync();
        rows.Select(r => r.GetProperty("userId").GetGuid()).Should().NotIntersectWith(supplierUserIds,
            "staff are the accounts with no SupplierId - a supplier's team is administered by that supplier");
    }

    [Fact]
    public async Task Active_sessions_are_counted_by_sign_in_and_only_while_alive()
    {
        var admin = await AdminAsync();
        var invitedId = await InviteAsync(admin, Roles.Evaluator);

        var now = DateTimeOffset.UtcNow;
        var twoTokenSignIn = Guid.CreateVersion7();
        RefreshToken Token(Guid familyId, DateTimeOffset expiresAt, DateTimeOffset? revokedAt = null) => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = invitedId,
            FamilyId = familyId,
            TokenHash = $"session-count-probe-{Guid.NewGuid():N}",
            CreatedAt = now.AddHours(-2),
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt,
        };

        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RefreshTokens.AddRange(
                Token(Guid.CreateVersion7(), expiresAt: now.AddHours(-1)),
                Token(Guid.CreateVersion7(), expiresAt: now.AddDays(7), revokedAt: now.AddMinutes(-5)),
                Token(twoTokenSignIn, expiresAt: now.AddDays(7)),
                Token(twoTokenSignIn, expiresAt: now.AddDays(7)),
                Token(Guid.CreateVersion7(), expiresAt: now.AddDays(7)));
            await db.SaveChangesAsync();
        }

        try
        {
            var listed = (await ListEveryStaffAccountAsync(admin))
                .Single(r => r.GetProperty("userId").GetGuid() == invitedId);
            listed.GetProperty("activeSessionCount").GetInt32().Should().Be(2,
                "two sign-ins are alive; the expired and the revoked ones are over, and a sign-in is one session however many live tokens it holds");

            var readBack = await admin.PutAsJsonAsync($"/api/v1/staff/{invitedId}/role", new { role = Roles.Evaluator });
            readBack.StatusCode.Should().Be(HttpStatusCode.OK, await readBack.Content.ReadAsStringAsync());
            (await readBack.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("activeSessionCount").GetInt32()
                .Should().Be(2, "the read-back after a staff change shows the same figure as the list");
        }
        finally
        {
            await using var cleanup = fixture.Services.CreateAsyncScope();
            var db = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RefreshTokens.Where(t => t.UserId == invitedId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Deactivating_a_staff_account_kills_its_sessions_and_reactivating_restores_it()
    {
        var admin = await AdminAsync();
        var invitedId = await InviteAsync(admin, Roles.Evaluator);

        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.CreateVersion7(),
                UserId = invitedId,
                FamilyId = Guid.CreateVersion7(),
                TokenHash = $"probe-{Guid.NewGuid():N}",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var deactivated = await admin.PostAsync($"/api/v1/staff/{invitedId}/deactivate", null);
        deactivated.StatusCode.Should().Be(HttpStatusCode.OK, await deactivated.Content.ReadAsStringAsync());

        await using (var check = fixture.Services.CreateAsyncScope())
        {
            var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.AsNoTracking().FirstAsync(u => u.Id == invitedId)).IsActive.Should().BeFalse();
            (await db.RefreshTokens.AsNoTracking().CountAsync(t => t.UserId == invitedId && t.RevokedAt == null))
                .Should().Be(0, "leaving sessions alive would make deactivated mean only \"cannot sign in again\"");

            (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.AggregateId == invitedId && a.Action == "staff_deactivated")).Should().BeTrue();
        }

        (await admin.PostAsync($"/api/v1/staff/{invitedId}/reactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var after = fixture.Services.CreateAsyncScope())
        {
            var db = after.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.AsNoTracking().FirstAsync(u => u.Id == invitedId)).IsActive.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Changing_a_role_replaces_it_and_ends_the_sessions_carrying_the_old_one()
    {
        var admin = await AdminAsync();
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var invitedId = await InviteAsync(admin, Roles.ProcurementOfficer, org.Id);

        var changed = await admin.PutAsJsonAsync($"/api/v1/staff/{invitedId}/role", new { role = Roles.ProcurementManager });
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString()
            .Should().Be(Roles.ProcurementManager);

        await using var scope = fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(invitedId.ToString());
        var roles = await userManager.GetRolesAsync(user!);

        roles.Should().BeEquivalentTo([Roles.ProcurementManager]);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
            a.AggregateId == invitedId && a.Action == "staff_role_changed"
            && a.FromState == Roles.ProcurementOfficer && a.ToState == Roles.ProcurementManager))
            .Should().BeTrue("who changed whose role, and to what");

        (await admin.PutAsJsonAsync($"/api/v1/staff/{invitedId}/role", new { role = Roles.SupplierAdmin }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_role_that_needs_an_organisation_is_refused_for_an_account_with_none()
    {
        var admin = await AdminAsync();
        var orglessId = await InviteAsync(admin, Roles.Evaluator);

        foreach (var role in new[] { Roles.ProcurementManager, Roles.ProcurementOfficer })
        {
            var refused = await admin.PutAsJsonAsync($"/api/v1/staff/{orglessId}/role", new { role });

            refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await refused.Content.ReadAsStringAsync());
            var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
            var error = problem.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
            error.GetProperty("field").GetString().Should().Be("organizationId");
            error.GetProperty("code").GetString().Should().Be(
                "ORGANIZATION_REQUIRED", $"a '{role}' needs an organisation, as the invitation says of one");
        }

        await using (var check = fixture.Services.CreateAsyncScope())
        {
            var userManager = check.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userManager.FindByIdAsync(orglessId.ToString());
            (await userManager.GetRolesAsync(user!)).Should().BeEquivalentTo([Roles.Evaluator], "a refused change changes nothing");

            var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.AggregateId == orglessId && a.Action == "staff_role_changed"))
                .Should().BeFalse("nothing changed, so the trail has nothing to record");
        }

        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var withOrgId = await InviteAsync(admin, Roles.Evaluator, org.Id);

        var changed = await admin.PutAsJsonAsync($"/api/v1/staff/{withOrgId}/role", new { role = Roles.ProcurementManager });

        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString()
            .Should().Be(Roles.ProcurementManager, "the same change is allowed for an account that has an organisation");
    }

    [Fact]
    public async Task Resetting_MFA_clears_the_enrolment_and_every_session()
    {
        var admin = await AdminAsync();
        var invitedId = await InviteAsync(admin, Roles.SystemAdmin);

        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var userManager = setup.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userManager.FindByIdAsync(invitedId.ToString());
            await userManager.SetTwoFactorEnabledAsync(user!, true);

            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.CreateVersion7(),
                UserId = invitedId,
                FamilyId = Guid.CreateVersion7(),
                TokenHash = $"mfa-probe-{Guid.NewGuid():N}",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        (await admin.PostAsync($"/api/v1/staff/{invitedId}/reset-mfa", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db2 = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db2.Users.AsNoTracking().FirstAsync(u => u.Id == invitedId)).TwoFactorEnabled
            .Should().BeFalse("the holder re-enrols on next sign-in");
        (await db2.RefreshTokens.AsNoTracking().CountAsync(t => t.UserId == invitedId && t.RevokedAt == null))
            .Should().Be(0, "a reset that left sessions alive would hand an attacker holding one a way to stay");

        (await db2.AuditLogs.AsNoTracking().AnyAsync(a =>
            a.AggregateId == invitedId && a.Action == "staff_mfa_reset")).Should().BeTrue();
    }

    [Fact]
    public async Task The_platform_cannot_be_locked_out_of_its_own_administration()
    {
        var (admin, ownId) = await StaffTestClient.CreateWithMfaAndIdAsync(fixture, Roles.SystemAdmin);

        (await admin.PostAsync($"/api/v1/staff/{ownId}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.PutAsJsonAsync($"/api/v1/staff/{ownId}/role", new { role = Roles.Evaluator }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.PostAsync($"/api/v1/staff/{ownId}/reset-mfa", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var otherAdminId = await InviteAsync(admin, Roles.SystemAdmin);
        (await admin.PostAsync($"/api/v1/staff/{otherAdminId}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.OK, "another administrator is removable while one remains");
    }

    [Fact]
    public async Task Nobody_without_admin_permission_can_administer_staff()
    {
        var admin = await AdminAsync();
        var targetId = await InviteAsync(admin, Roles.Evaluator);

        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.ProcurementManager, Roles.MinistryViewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.GetAsync("/api/v1/staff")).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{role} does not hold admin.users.manage");
            (await staff.PostAsync($"/api/v1/staff/{targetId}/deactivate", null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await staff.PostAsync($"/api/v1/staff/{targetId}/reset-mfa", null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Staff Admin Outsider");
        (await supplier.GetAsync("/api/v1/staff")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await admin.GetAsync("/api/v1/staff")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_suppliers_user_is_not_a_staff_account_and_answers_404_rather_than_403()
    {
        var admin = await AdminAsync();
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Not Staff Co");

        Guid supplierUserId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            supplierUserId = await db.Users.Where(u => u.SupplierId != null).Select(u => u.Id).FirstAsync();
        }

        (await admin.PostAsync($"/api/v1/staff/{supplierUserId}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsync($"/api/v1/staff/{Guid.CreateVersion7()}/reset-mfa", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "and an id that is nobody answers the same way");
    }
}
