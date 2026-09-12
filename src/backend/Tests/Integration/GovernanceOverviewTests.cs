using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Awards;
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
    /// <summary>
    /// Sets the D-6 commercial-visibility flag and puts back whatever it was when the scope ends.
    ///
    /// <para>T-073: the flag is a seeded row in a database shared by every integration class, and the
    /// restore used to be a bare statement at the end of the test - so a failing assertion above it
    /// left the Ministry's commercial figures disclosed for everything that ran afterwards. Reading
    /// the value first means the restore does not need to know what the seed says.</para>
    /// </summary>
    private async Task<IAsyncDisposable> CommercialVisibilityAsync(bool enabled)
    {
        bool original;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            original = await db.Set<SupplierFieldConfig>().AsNoTracking()
                .Where(c => c.Category == FieldConfigCategory.GovernanceVisibility && c.FieldCode == "commercialValues")
                .Select(c => c.IsEnabled)
                .SingleAsync();
        }

        await SetCommercialVisibilityAsync(enabled);
        return new RestoreVisibility(this, original);
    }

    private sealed class RestoreVisibility(GovernanceOverviewTests tests, bool original) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(tests.SetCommercialVisibilityAsync(original));
    }

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
        await using var visibility = await CommercialVisibilityAsync(false);

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

        await using (await CommercialVisibilityAsync(false))
        {
            var withheld = await ministry.GetFromJsonAsync<JsonElement>("/api/v1/ministry/overview");
            withheld.GetProperty("totalAwardedValue").ValueKind.Should().Be(JsonValueKind.Null);
        }

        await using (await CommercialVisibilityAsync(true))
        {
            var disclosed = await ministry.GetFromJsonAsync<JsonElement>("/api/v1/ministry/overview");

            disclosed.GetProperty("commercialValuesVisible").GetBoolean().Should().BeTrue();
            disclosed.GetProperty("totalAwardedValue").ValueKind.Should().NotBe(JsonValueKind.Null,
                "the flag is the only thing standing between the Ministry and this figure");
        }
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

    /// <summary>
    /// The headline tile counts AWARDS, not award rows.
    ///
    /// <para>Found on the demonstration database during a walkthrough: the governance dashboard said
    /// 18 while the Awards &amp; spend screen said 17, and the total value beside the 18 was computed
    /// from the 17. One of the rows was a recommendation nobody had approved.</para>
    ///
    /// <para>The arrangement is the control: a Recommended row is inserted, so a handler that counts
    /// rows fails this test rather than passing because the database happened to hold none.</para>
    /// </summary>
    [Fact]
    public async Task A_recommendation_nobody_approved_is_not_counted_as_an_award()
    {
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        var before = (await ministry.GetFromJsonAsync<JsonElement>("/api/v1/ministry/overview"))
            .GetProperty("totalAwards").GetInt32();

        var seeded = await EvaluationSeed.CreateAsync(fixture, "Gov Recommended");
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rfqId = await db.Rfqs.AsNoTracking()
                .Where(r => r.ReferenceCode == seeded.RfqCode).Select(r => r.Id).SingleAsync();
            db.Awards.Add(Award.Recommend(rfqId, seeded.ProposalId,
                "توصية لم تُعتمد", "A recommendation nobody approved", Guid.CreateVersion7()));
            await db.SaveChangesAsync();
        }

        var after = (await ministry.GetFromJsonAsync<JsonElement>("/api/v1/ministry/overview"))
            .GetProperty("totalAwards").GetInt32();

        after.Should().Be(before,
            "a Recommended row is not an award, and the tile sits beside a value computed from Awarded only");

        // And the count is the Awarded count in storage, not merely unchanged by this one row.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var awarded = await db.Awards.AsNoTracking().CountAsync(a => a.State == AwardState.Awarded);
            var rows = await db.Awards.AsNoTracking().CountAsync();

            rows.Should().BeGreaterThan(awarded, "the arrangement above must actually have left a non-Awarded row");
            after.Should().Be(awarded);
        }
    }

    [Fact]
    public void The_ministry_viewer_holds_governance_read_and_report_read_and_nothing_else()
    {
        // The permission set itself, asserted rather than assumed - it was empty, and an empty set is
        // how a persona ends up able to log in and reach nothing.
        //
        // report.read was ADDED in batch 11, deliberately, and this test is the record of that decision
        // rather than a casualty of it. SCR-604's reports screen was reachable by no persona who could
        // legitimately read it, and the grant was checked before it was made: both report DTOs carry counts
        // and states only, no bid values and no supplier identities, so A-10/D-6's aggregate-only rule for
        // the Ministry survives it. The word "nothing else" is the part still worth asserting - this persona
        // must not accumulate rfq.read or anything that reaches an individual tender.
        Roles.DefaultPermissions[Roles.MinistryViewer].Should()
            .BeEquivalentTo(new[] { Permissions.GovernanceRead, Permissions.ReportRead });
        Roles.DefaultPermissions[Roles.MinistryViewer].Should().NotContain(Permissions.RfqRead,
            "A-10/D-6: the Ministry sees aggregates, never an individual tender");
    }
}
