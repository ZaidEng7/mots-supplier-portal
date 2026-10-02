// A person starting again the send of an award's purchase order that failed, through the route the Operations page
// presses.
//
// THE SYSTEM ADMINISTRATOR IS THE CASE WORTH A TEST. They are the only role that holds integration.retry by default and
// they belong to no organisation, while the retry used to find its award only through the caller's organisation, so it
// answered not found to exactly the person the page offers the button to, on an award the same page had just listed.
// The retry here is pressed by one, through the real route, and the trail must name them rather than the system.
//
// AN ORGANISATION'S ROLE KEEPS ITS SCOPE. A deployment may grant integration.retry to an organisation's role, and the
// wider path is for the caller with no organisation only, so such a role retries its own organisation's award and not
// another's. That is tested by granting the permission to the procurement manager for the length of the test and
// putting the role back afterwards. The refused retry must leave the award failed, and the control is the manager of
// the award's own organisation retrying the same award successfully.
//
// HAVING NO ORGANISATION IS NOT THE WHOLE TEST. The wider path also needs integration.retry, and it is never taken by a
// supplier's account, which has no organisation either. The route refuses a caller without the permission before the
// handler runs, so that rule can only be seen in the handler, and the supplier's case is asked there too rather than by
// editing a role every supplier in the suite holds. Both must answer not found and leave the award failed. The control
// is the same handler with a platform administrator's scope retrying the same award, so the two refusals are not
// refusals of everything.
//
// THE AWARDS COME FROM FailedAwardSeed, forced into a failed send rather than driven through approval, issue and a
// failing adapter, and each is removed at the end of its test, so that no ERP sync job run elsewhere in the suite picks
// up the Requested it was retried into.
//
// Nothing here calls an ERP.

namespace MotsSupplierPortal.Tests.Integration.Awards;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Awards;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Awards;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class AwardErpSyncRetryTests(PostgresApiFixture fixture)
{
    private async Task<Award> ReadAsync(Guid awardId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Awards.AsNoTracking().SingleAsync(a => a.Id == awardId);
    }

    private async Task<List<MotsSupplierPortal.Domain.Audit.AuditLog>> RetriesAsync(Guid awardId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateId == awardId && a.Action == "award.erp_po_retried")
            .ToListAsync();
    }

    [Fact]
    public async Task A_system_administrator_retries_a_failed_send_and_the_trail_names_them()
    {
        var (rfqCode, awardId, _) = await FailedAwardSeed.CreateAsync(fixture, "Retry Admin");
        var (admin, adminId) = await StaffTestClient.CreateWithMfaAndIdAsync(fixture, Roles.SystemAdmin);

        try
        {
            var response = await admin.PostAsync($"/api/v1/rfqs/{rfqCode}/award/retry-erp-sync", content: null);

            response.StatusCode.Should().Be(
                HttpStatusCode.OK,
                "the system administrator belongs to no organisation, and the retry must not look the award up through one");

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("erpSyncStatus").GetString().Should().Be("Requested");
            body.GetProperty("rfqReferenceCode").GetString().Should().Be(rfqCode);

            (await ReadAsync(awardId)).ErpSyncStatus.Should().Be(ErpSyncStatus.Requested);

            var retried = (await RetriesAsync(awardId)).Should().ContainSingle().Subject;
            retried.ActorUserId.Should().Be(adminId, "a person started it, and the trail says who");
            retried.ToState.Should().Be("Requested");
            retried.ReferenceCode.Should().Be(rfqCode);
        }
        finally
        {
            await FailedAwardSeed.RemoveAsync(fixture, awardId);
        }
    }

    [Fact]
    public async Task A_role_granted_integration_retry_retries_its_own_organisations_award_and_not_another_s()
    {
        var (rfqCode, awardId, orgId) = await FailedAwardSeed.CreateAsync(fixture, "Retry Scoped");
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

            var otherOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
            var outsider = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, otherOrg.Id);

            var refused = await outsider.PostAsync($"/api/v1/rfqs/{rfqCode}/award/retry-erp-sync", content: null);

            refused.StatusCode.Should().Be(
                HttpStatusCode.NotFound,
                "a caller with an organisation is served through it, whatever permission their role was granted");
            (await ReadAsync(awardId)).ErpSyncStatus.Should().Be(ErpSyncStatus.Failed);
            (await RetriesAsync(awardId)).Should().BeEmpty();

            var owner = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, orgId);

            var retried = await owner.PostAsync($"/api/v1/rfqs/{rfqCode}/award/retry-erp-sync", content: null);

            retried.StatusCode.Should().Be(
                HttpStatusCode.OK, "the award's own organisation still retries it through the scoped load");
            (await ReadAsync(awardId)).ErpSyncStatus.Should().Be(ErpSyncStatus.Requested);
        }
        finally
        {
            var restored = await admin.PutAsJsonAsync(
                $"/api/v1/admin/roles/{Roles.ProcurementManager}/permissions", new { permissions = original });
            restored.StatusCode.Should().Be(HttpStatusCode.OK, "a role this test edited must be put back");
            await FailedAwardSeed.RemoveAsync(fixture, awardId);
        }
    }

    [Fact]
    public async Task A_tender_that_does_not_exist_is_not_found_for_the_system_administrator()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.PostAsync("/api/v1/rfqs/RFQ-NOT-A-TENDER/award/retry-erp-sync", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_supplier_or_a_caller_without_integration_retry_is_not_served_across_the_registry()
    {
        var (rfqCode, awardId, _) = await FailedAwardSeed.CreateAsync(fixture, "Retry Refused");

        try
        {
            var refusedScopes = new (string Who, IScopeContext Scope)[]
            {
                ("a supplier's account granted integration.retry", new TestScope(supplierId: Guid.CreateVersion7(), retries: true)),
                ("a caller with no organisation and no integration.retry", new TestScope(supplierId: null, retries: false)),
            };

            foreach (var (who, refusedScope) in refusedScopes)
            {
                (await RetryThroughTheHandlerAsync(refusedScope, rfqCode))
                    .Should().BeOfType<AwardMutationResult.NotFoundOrOutOfScope>($"{who} is not the platform administrator");
                (await ReadAsync(awardId)).ErpSyncStatus.Should().Be(ErpSyncStatus.Failed);
            }

            (await RetryThroughTheHandlerAsync(new TestScope(supplierId: null, retries: true), rfqCode))
                .Should().BeOfType<AwardMutationResult.Success>("the same award is found for the platform administrator");
            (await ReadAsync(awardId)).ErpSyncStatus.Should().Be(ErpSyncStatus.Requested);
        }
        finally
        {
            await FailedAwardSeed.RemoveAsync(fixture, awardId);
        }
    }

    private async Task<AwardMutationResult> RetryThroughTheHandlerAsync(IScopeContext caller, string rfqCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = new RetryErpSyncHandler(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            caller,
            scope.ServiceProvider.GetRequiredService<IAuditLogger>());
        return await handler.HandleAsync(new RetryErpSyncCommand(rfqCode), CancellationToken.None);
    }

    private sealed class TestScope(Guid? supplierId, bool retries) : IScopeContext
    {
        public Guid? UserId { get; } = Guid.CreateVersion7();
        public Guid? SupplierId { get; } = supplierId;
        public Guid? OrganizationId => null;
        public bool IsAuthenticated => true;
        public bool HasPermission(string permission) => retries && permission == Permissions.IntegrationRetry;
    }
}
