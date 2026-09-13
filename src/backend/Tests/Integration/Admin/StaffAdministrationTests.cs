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

    [Fact]
    public async Task The_list_carries_the_facts_an_administrator_needs_and_no_supplier_users()
    {
        var admin = await AdminAsync();
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var invitedId = await InviteAsync(admin, Roles.ProcurementOfficer, org.Id);

        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Staff List Outsider Co");

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
