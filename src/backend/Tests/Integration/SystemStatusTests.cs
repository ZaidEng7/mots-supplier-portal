using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// SCR-045's chrome banner reads this, and what it may say depends on who is asking.
///
/// <para>Both directions of every claim: that the flag can be true, and that it can be false for a
/// caller not entitled to it. A status endpoint that always answered "fine" would pass a one-sided
/// test and render a banner that never appears.</para>
/// </summary>
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
        // system_admin holds integration.retry, so this is a fact they can act on. LoggingOutboxTransport
        // is the only registered transport (T-089), so the flag being TRUE is the honest answer here
        // and proves the banner can fire at all.
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        (await StatusAsync(admin)).GetProperty("erpNotConfigured").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_supplier_is_not_told_about_the_ministry_s_deployment()
    {
        // The control for the test above, and the reason the flag is permission-gated rather than
        // global: a supplier told the ministry has no ERP integration has learned something about the
        // ministry, not about their own bid.
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
}
