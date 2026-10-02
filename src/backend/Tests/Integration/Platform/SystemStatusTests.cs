// SCR-045's chrome banner reads this endpoint, and what it may say depends on who is asking.
//
// Both directions of every claim: that the flag can be true, and that it can be false for a caller not entitled
// to it. A status endpoint that always answered "fine" would pass a one-sided test and render a banner that
// never appears.
//
// An administrator is told that no real ERP transport is configured. system_admin holds integration.retry, so
// this is a fact they can act on, and LoggingOutboxTransport is the only registered transport (T-089), so the
// flag being TRUE is the honest answer and proves the banner can fire at all.
//
// A supplier is not told about the ministry's deployment, which is the control for the test above and the reason
// the flag is permission-gated rather than global: a supplier told the ministry sends no purchase orders to the
// ERP has learned something about the ministry, not about their own bid.
//
// An officer with no failed sync in their organization sees no degradation, and the endpoint refuses an
// anonymous caller.
//
// THE PLATFORM ADMINISTRATOR IS TOLD WHEN ANY AWARD'S SEND HAS FAILED. They belong to no organisation, so the
// banner used to stay silent for them whatever the Operations page listed. A failed award from FailedAwardSeed
// turns their flag on, and a reviewer, who has no organisation either but does not hold integration.retry, is
// told nothing about the same award. Each award is removed at the end of its test.
//
// NOR IS AN ACCOUNT WITH NO ORGANISATION THAT HOLDS ONLY integration.retry. The wide scope also needs
// admin.integrations.manage, the system administrator's alone by default, because having no organisation is not
// being the platform's. The procurement manager's role is granted integration.retry for the length of the test and
// put back afterwards, and a manager with no organisation signed in under it is told nothing about the failed
// award, while the system administrator asking at the same moment is told.
//
// AN ORGANISATION'S CALLER IS TOLD ABOUT THEIR OWN ORGANISATION'S SENDS ONLY, WHATEVER THEY HOLD. Asked through the
// handler with a scope that holds every permission in the catalogue, so that only the organisation stands between
// the caller and the registry: from another organisation the failed award is not theirs and the flag is off, and
// from the award's own organisation it is on.
//
// THE CONTROL, NO FAILED AWARD AT ALL, IS ASSERTED THROUGH THE HANDLER INSIDE A TRANSACTION THAT IS ROLLED BACK.
// The database is shared, and other classes leave failed awards behind on purpose (the notification tests fail
// every award waiting to be sent), so a registry with none cannot be had through the route without changing
// rows other classes read. Inside the transaction every failed award is marked synced, the handler is asked
// on that same connection with an administrator's scope, and the rollback puts them all back. The seeded award
// is still there, synced, so a handler that asked whether any award exists, rather than any failed one, is
// caught too.

namespace MotsSupplierPortal.Tests.Integration.Platform;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Platform;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class SystemStatusTests(PostgresApiFixture fixture)
{
    private static async Task<JsonElement> StatusAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/system/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task An_administrator_is_told_that_no_real_ERP_transport_is_configured()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        (await StatusAsync(admin)).GetProperty("erpNotConfigured").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_supplier_is_not_told_about_the_ministry_s_deployment()
    {
        var (supplier, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(
            fixture, $"Status {Guid.NewGuid():N}"[..30]);

        (await StatusAsync(supplier)).GetProperty("erpNotConfigured").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task An_officer_with_no_failed_sync_in_their_organization_sees_no_degradation()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        (await StatusAsync(officer)).GetProperty("erpDegraded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task The_endpoint_refuses_an_anonymous_caller()
    {
        (await fixture.CreateRawClient().GetAsync("/api/v1/system/status"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_administrator_is_told_when_any_award_s_send_has_failed_and_a_reviewer_is_not()
    {
        var failed = await FailedAwardSeed.CreateAsync(fixture, "Status Failed");

        try
        {
            var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
            var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

            (await StatusAsync(admin)).GetProperty("erpDegraded").GetBoolean().Should().BeTrue(
                "the administrator belongs to no organisation, and the failed award is theirs to retry");
            (await StatusAsync(reviewer)).GetProperty("erpDegraded").GetBoolean().Should().BeFalse(
                "having no organisation is not a grant: the reviewer does not hold integration.retry");
        }
        finally
        {
            await FailedAwardSeed.RemoveAsync(fixture, failed.AwardId);
        }
    }

    [Fact]
    public async Task An_administrator_sees_no_degradation_when_no_award_has_failed()
    {
        var failed = await FailedAwardSeed.CreateAsync(fixture, "Status Control");

        try
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handler = new SystemStatusHandler(
                db,
                new TestScope(organizationId: null, Permissions.IntegrationRetry, Permissions.AdminIntegrationsManage),
                scope.ServiceProvider.GetRequiredService<IOutboxTransport>());

            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                (await handler.HandleAsync(CancellationToken.None)).ErpDegraded.Should().BeTrue(
                    "the seeded award has failed, so the same handler on the same connection must say so first");

                await db.Awards.Where(a => a.ErpSyncStatus == ErpSyncStatus.Failed)
                    .ExecuteUpdateAsync(set => set.SetProperty(a => a.ErpSyncStatus, ErpSyncStatus.Synced));

                (await handler.HandleAsync(CancellationToken.None)).ErpDegraded.Should().BeFalse(
                    "no award in the registry has failed, so there is nothing for the administrator to retry");

                await transaction.RollbackAsync();
            }

            (await db.Awards.AsNoTracking().SingleAsync(a => a.Id == failed.AwardId)).ErpSyncStatus
                .Should().Be(ErpSyncStatus.Failed, "the rollback must put back every award the control marked synced");
        }
        finally
        {
            await FailedAwardSeed.RemoveAsync(fixture, failed.AwardId);
        }
    }

    [Fact]
    public async Task A_manager_with_no_organisation_granted_integration_retry_is_not_told_about_the_registry()
    {
        var failed = await FailedAwardSeed.CreateAsync(fixture, "Status Orgless");
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var roles = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/roles");
        var original = roles.GetProperty("roles").EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == Roles.ProcurementManager)
            .GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()!)
            .ToArray();

        try
        {
            var granted = await admin.PutAsJsonAsync(
                $"/api/v1/admin/roles/{Roles.ProcurementManager}/permissions",
                new { permissions = original.Append(Permissions.IntegrationRetry).Distinct().ToArray() });
            granted.StatusCode.Should().Be(HttpStatusCode.OK);

            var orgless = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, organizationId: null);

            (await StatusAsync(orgless)).GetProperty("erpDegraded").GetBoolean().Should().BeFalse(
                "no organisation and integration.retry is not the platform administrator, who also holds admin.integrations.manage");
            (await StatusAsync(admin)).GetProperty("erpDegraded").GetBoolean().Should().BeTrue(
                "the system administrator is still told about the same failed award");
        }
        finally
        {
            var restored = await admin.PutAsJsonAsync(
                $"/api/v1/admin/roles/{Roles.ProcurementManager}/permissions", new { permissions = original });
            restored.StatusCode.Should().Be(HttpStatusCode.OK, "a role this test edited must be put back");
            await FailedAwardSeed.RemoveAsync(fixture, failed.AwardId);
        }
    }

    [Fact]
    public async Task An_organisation_s_caller_is_told_only_about_their_own_organisation_whatever_they_hold()
    {
        var failed = await FailedAwardSeed.CreateAsync(fixture, "Status Scoped");

        try
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var transport = scope.ServiceProvider.GetRequiredService<IOutboxTransport>();
            var everything = Permissions.All.ToArray();

            var otherOrganisation = new SystemStatusHandler(
                db, new TestScope(organizationId: Guid.CreateVersion7(), everything), transport);
            (await otherOrganisation.HandleAsync(CancellationToken.None)).ErpDegraded.Should().BeFalse(
                "the failed award belongs to another organisation, and a caller with one is never served the registry");

            var ownOrganisation = new SystemStatusHandler(
                db, new TestScope(organizationId: failed.OrgId, everything), transport);
            (await ownOrganisation.HandleAsync(CancellationToken.None)).ErpDegraded.Should().BeTrue(
                "the failed award is the caller's own organisation's");
        }
        finally
        {
            await FailedAwardSeed.RemoveAsync(fixture, failed.AwardId);
        }
    }

    private sealed class TestScope(Guid? organizationId, params string[] permissions) : IScopeContext
    {
        public Guid? UserId { get; } = Guid.CreateVersion7();
        public Guid? SupplierId => null;
        public Guid? OrganizationId { get; } = organizationId;
        public bool IsAuthenticated => true;
        public bool HasPermission(string permission) => permissions.Contains(permission, StringComparer.Ordinal);
    }
}
