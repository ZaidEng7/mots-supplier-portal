using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// B-1/BRULE-011, recorded rather than pretended.
///
/// <para>BRULE-011 says a supplier's <c>ExternalId</c> is assigned "only after onboarding approval and
/// successful ERP upsert ACK". It passes today because <b>nothing exercises it</b>:
/// <c>Supplier.MarkSynced</c> is never called, because the only <c>IOutboxTransport</c> is a stand-in that
/// writes a log line, so no ACK ever arrives. A rule that cannot be violated is not the same as one that
/// is satisfied, and the batch-9 sweep called this out as the sharpest kind of false green.</para>
///
/// <para><b>These tests assert the ABSENCE deliberately.</b> They are not a claim that ERP sync works -
/// they are the opposite, written so that the day someone registers a real transport they go red and say
/// what has to be built alongside it: the ACK path that calls MarkSynced, and BRULE-011's own guard that
/// no ExternalId is assigned before approval.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ErpSyncVacuityTests(PostgresApiFixture fixture)
{
    [Fact]
    public void The_registered_outbox_transport_is_the_logging_stand_in()
    {
        using var scope = fixture.Services.CreateScope();
        var transport = scope.ServiceProvider.GetRequiredService<IOutboxTransport>();

        transport.Should().BeOfType<LoggingOutboxTransport>(
            "when this fails, a real ERP transport has been registered - and BRULE-011 then needs the ACK "
            + "path that calls Supplier.MarkSynced, plus a guard that no ExternalId is assigned before "
            + "onboarding approval. Neither exists yet, which is what this test records.");
    }

    [Fact]
    public async Task No_supplier_has_ever_been_assigned_an_ExternalId()
    {
        // An APPROVED supplier of this test's own making, so the claim holds when the class runs alone -
        // the non-vacuity guard below fired exactly that way on the first run, which is what it is for.
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Erp Vacuity {Guid.NewGuid():N}"[..24]);
        await using (var approve = fixture.Services.CreateAsyncScope())
        {
            var setup = approve.ServiceProvider.GetRequiredService<AppDbContext>();
            var newest = await setup.Suppliers.OrderByDescending(s => s.CreatedAt).FirstAsync();
            await setup.Suppliers.Where(s => s.Id == newest.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
                    .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));
        }

        // The consequence, measured in storage rather than argued from the registration: every supplier in
        // the database - including approved ones - has a null ExternalId and a SyncStatus that never
        // reached Synced.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Suppliers.AsNoTracking().CountAsync(s => s.ExternalId != null))
            .Should().Be(0, "MarkSynced is unreachable, so BRULE-011 is vacuously true");
        (await db.Suppliers.AsNoTracking().CountAsync(s => s.SyncStatus == SupplierSyncStatus.Synced))
            .Should().Be(0);

        // And there ARE approved suppliers to be wrong about - otherwise the two counts above would pass
        // on an empty set and prove nothing.
        (await db.Suppliers.AsNoTracking().CountAsync(s => s.OnboardingState == SupplierOnboardingState.Approved))
            .Should().BeGreaterThan(0, "the assertions above are about approved suppliers, so some must exist");
    }

    [Fact]
    public async Task The_admin_dashboard_says_the_ERP_transport_is_not_configured()
    {
        // Without this the outbox tile is an artifact asserting something untrue: messages drain, which
        // reads as "the integration is working", while nothing has left the building.
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, MotsSupplierPortal.Domain.Identity.Roles.SystemAdmin);

        var overview = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/admin/overview");

        overview.GetProperty("outbox").GetProperty("erpTransportConfigured").GetBoolean()
            .Should().BeFalse("an operator is entitled to know that a draining outbox is reaching a log file");
    }
}
