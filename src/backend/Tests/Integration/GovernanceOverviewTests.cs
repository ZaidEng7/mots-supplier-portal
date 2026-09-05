using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// EPIC-18/FR-DSH-005/SCR-600 under D-6. Before this, <c>ministry_viewer</c> held an EMPTY permission
/// set - the persona could log in and reach nothing.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class GovernanceOverviewTests(PostgresApiFixture fixture)
{
    /// <summary>
    /// Flips D-6's policy flag, and ASSERTS it flipped.
    ///
    /// <para>The row is global, so this is shared state between tests. The first version returned the
    /// ExecuteUpdate count to nobody: a write that matched no row was a silent no-op, and the test
    /// then asserted against whatever the flag already was - which passed alone and failed in the full
    /// run. Asserting the row count turns that into a failure that names itself.</para>
    /// </summary>
    private async Task SetCommercialVisibilityAsync(bool enabled)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await db.Set<SupplierFieldConfig>()
            .Where(c => c.Category == FieldConfigCategory.GovernanceVisibility && c.FieldCode == "commercialValues")
            .ExecuteUpdateAsync(p => p.SetProperty(c => c.IsEnabled, enabled));

        updated.Should().Be(1,
            "the GovernanceVisibility flag row is seeded, so a write that matches nothing means the " +
            "seed is missing rather than the assertion being wrong");

        // Read back through the same path the handler uses, so the test never proceeds on a write it
        // cannot see.
        (await db.Set<SupplierFieldConfig>().AsNoTracking()
            .Where(c => c.Category == FieldConfigCategory.GovernanceVisibility && c.FieldCode == "commercialValues")
            .Select(c => c.IsEnabled).FirstAsync())
            .Should().Be(enabled);
    }

    [Fact]
    public async Task The_ministry_sees_cross_organization_aggregates_and_no_commercial_figure_by_default()
    {
        await SetCommercialVisibilityAsync(false);

        // Two organizations, so "cross-organization" is a claim with something to cross.
        await EvaluationSeed.CreateAsync(fixture, "Gov One");
        await EvaluationSeed.CreateAsync(fixture, "Gov Two");

        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        var response = await ministry.GetAsync("/api/v1/ministry/overview");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("totalSuppliers").GetInt32().Should().BeGreaterThan(1);
        body.GetProperty("totalRfqs").GetInt32().Should().BeGreaterThan(1,
            "the counts span organizations - BRULE-086's whole grant");
        body.GetProperty("rfqsByState").GetArrayLength().Should().BeGreaterThan(0);

        // D-6/BRULE-087, seeded off: null, not zero. "Policy withholds this" and "nothing has been
        // awarded" are different facts and a reader must be able to tell them apart.
        body.GetProperty("commercialValuesVisible").GetBoolean().Should().BeFalse();
        body.GetProperty("totalAwardedValue").ValueKind.Should().Be(JsonValueKind.Null);

        // And no row identifies anyone. BRULE-086 grants aggregates only, so a supplier name or an RFQ
        // code appearing here would be the disclosure the rule exists to prevent.
        var raw = body.ToString();
        foreach (var identifying in new[] { "RFQ-", "SUP-", "PRP-", "displayName", "referenceCode" })
        {
            raw.Should().NotContain(identifying,
                $"'{identifying}' identifies a row, and the Ministry's grant is aggregate-only");
        }
    }

    [Fact]
    public async Task The_commercial_figure_appears_only_when_the_policy_flag_is_on()
    {
        // The guard both ways, on one flag - which is exactly what D-6 promises: MOT Legal's answer
        // flips a value rather than commissioning an epic.
        await EvaluationSeed.CreateAsync(fixture, "Gov Flag");
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);

        await SetCommercialVisibilityAsync(false);
        var withheld = await ministry.GetFromJsonAsync<JsonElement>("/api/v1/ministry/overview");
        withheld.GetProperty("totalAwardedValue").ValueKind.Should().Be(JsonValueKind.Null);

        await SetCommercialVisibilityAsync(true);
        var disclosed = await ministry.GetFromJsonAsync<JsonElement>("/api/v1/ministry/overview");

        disclosed.GetProperty("commercialValuesVisible").GetBoolean().Should().BeTrue();
        disclosed.GetProperty("totalAwardedValue").ValueKind.Should().NotBe(JsonValueKind.Null,
            "the flag is the only thing standing between the Ministry and this figure");

        // Put it back, so this test does not leave a policy flag flipped for the suite.
        await SetCommercialVisibilityAsync(false);
    }

    [Fact]
    public async Task Nobody_else_can_read_the_governance_overview()
    {
        // A cross-organization read that skips row scoping must be reachable only by the persona whose
        // purpose is to skip it. Each of these holds a permission that reads RFQs or reports within
        // their own organization, and none of them holds governance.read.
        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.ProcurementManager, Roles.OnboardingReviewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.GetAsync("/api/v1/ministry/overview")).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{role} does not hold governance.read");
        }

        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Gov Outsider Co");
        (await supplier.GetAsync("/api/v1/ministry/overview")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // The control: the persona the rule names does get it.
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        (await ministry.GetAsync("/api/v1/ministry/overview")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void The_ministry_viewer_holds_governance_read_and_nothing_else()
    {
        // The permission set itself, asserted rather than assumed - it was empty, and an empty set is
        // how a persona ends up able to log in and reach nothing.
        Roles.DefaultPermissions[Roles.MinistryViewer].Should().BeEquivalentTo(new[] { Permissions.GovernanceRead });
    }
}
