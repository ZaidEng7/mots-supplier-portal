using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// SCR-400 / FR-DSH-008 / RISK-004.
///
/// <para><b>A count is a leak.</b> "Active RFQs: 14" that includes another organization's rows
/// discloses volume without disclosing a single row, and no list-level test would catch it. Every
/// assertion here is on a NUMBER, each with an owner control beside it so a zero cannot pass because
/// the query is broken.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProcurementDashboardTests(PostgresApiFixture fixture)
{
    private sealed record Org(HttpClient Officer, HttpClient Manager, Guid OrgId);

    private async Task<Org> OrgWithRfqsAsync(string label, int draftCount)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        for (var i = 0; i < draftCount; i++)
        {
            var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
            {
                titleAr = "طلب", titleEn = $"{label} {i}",
                descriptionAr = (string?)null, descriptionEn = (string?)null,
                currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
                submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
                submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
                clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
            });
            created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        }

        return new Org(officer, manager, org.Id);
    }

    private static async Task<JsonElement> DashboardAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/v1/procurement/dashboard{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task The_counts_are_this_organizations_and_never_another_organizations()
    {
        var mine = await OrgWithRfqsAsync("CountMine", draftCount: 3);
        var theirs = await OrgWithRfqsAsync("CountTheirs", draftCount: 5);

        var myDashboard = await DashboardAsync(mine.Officer);
        var theirDashboard = await DashboardAsync(theirs.Officer);

        // The control: my own rows really are counted, so a scoped number is not just a zero.
        myDashboard.GetProperty("kpis").GetProperty("activeRfqs").GetInt32().Should().Be(3,
            "control: the officer's own organization's RFQs are counted");

        // The leak that no list-level test would catch: a count that quietly includes the other org.
        theirDashboard.GetProperty("kpis").GetProperty("activeRfqs").GetInt32().Should().Be(5,
            "each organization's count is its own - 8 here would disclose the other's volume");

        // And the board, column by column: Draft is the only populated state in either org.
        var myDraft = myDashboard.GetProperty("pipeline").EnumerateArray()
            .Single(c => c.GetProperty("state").GetString() == nameof(RfqState.Draft));
        myDraft.GetProperty("count").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task A_caller_with_no_organization_gets_404_rather_than_an_empty_dashboard()
    {
        // §9.2: out-of-scope reads as not-found. An empty dashboard would still assert that an
        // organization exists and is idle.
        var orphan = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, organizationId: null);

        var response = await orphan.GetAsync("/api/v1/procurement/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Awaiting_my_action_differs_between_an_officer_and_a_manager_on_the_same_data()
    {
        // The property that makes the tile mean anything. It is an INVENTION - §10 names the tile and
        // defines nothing, and there is no per-user ownership to derive it from - so the test that
        // matters is that it is not silently org-wide: a Draft RFQ awaits the officer who can submit
        // it for review, not the manager who cannot.
        var org = await OrgWithRfqsAsync("Awaiting", draftCount: 2);

        var officerView = await DashboardAsync(org.Officer);
        var managerView = await DashboardAsync(org.Manager);

        officerView.GetProperty("kpis").GetProperty("awaitingMyAction").GetInt32().Should().Be(2,
            "Draft is waiting on rfq.submit_review, which the officer holds");
        managerView.GetProperty("kpis").GetProperty("awaitingMyAction").GetInt32().Should().Be(0,
            "the manager cannot submit an RFQ for review, so none of these are waiting on them");

        // The control against the tile silently becoming Active RFQs: both personas see the same
        // total, and only the per-user number differs.
        managerView.GetProperty("kpis").GetProperty("activeRfqs").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Only_the_manager_is_offered_the_approvals_card()
    {
        // §10: "Manager also gets an Approvals card". Decided from the permission, so the affordance
        // and the API agree about who may approve.
        var org = await OrgWithRfqsAsync("ApprovalsCard", draftCount: 1);

        (await DashboardAsync(org.Manager)).GetProperty("showsApprovals").GetBoolean().Should().BeTrue();
        (await DashboardAsync(org.Officer)).GetProperty("showsApprovals").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task The_period_filter_keeps_rows_that_were_never_published()
    {
        // The decision this filter forces, stated as a test: an RFQ that has never been published has
        // no publishedAt to compare, and excluding it would empty the board's left-hand columns the
        // moment a period is chosen - Draft, InternalReview and Approved would vanish from an
        // officer's own dashboard.
        var org = await OrgWithRfqsAsync("Period", draftCount: 2);

        var lastYear = DateTimeOffset.UtcNow.AddYears(-1).ToString("O");
        var lastMonth = DateTimeOffset.UtcNow.AddMonths(-1).ToString("O");

        var filtered = await DashboardAsync(org.Officer, $"?from={Uri.EscapeDataString(lastYear)}&to={Uri.EscapeDataString(lastMonth)}");

        filtered.GetProperty("kpis").GetProperty("activeRfqs").GetInt32().Should().Be(2,
            "a window entirely in the past still shows unpublished drafts, which have no date to fall outside it");
    }

    [Fact]
    public async Task The_period_filter_does_exclude_a_published_RFQ_outside_the_window()
    {
        // The other direction, and the control for the test above: the filter is not a no-op.
        var org = await OrgWithRfqsAsync("PeriodPublished", draftCount: 1);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Rfqs.Where(r => r.OrganizationId == org.OrgId)
                .ExecuteUpdateAsync(p => p.SetProperty(r => r.PublishedAt, DateTimeOffset.UtcNow));
        }

        var lastYear = DateTimeOffset.UtcNow.AddYears(-1).ToString("O");
        var lastMonth = DateTimeOffset.UtcNow.AddMonths(-1).ToString("O");

        var filtered = await DashboardAsync(org.Officer, $"?from={Uri.EscapeDataString(lastYear)}&to={Uri.EscapeDataString(lastMonth)}");

        filtered.GetProperty("kpis").GetProperty("activeRfqs").GetInt32().Should().Be(0,
            "a published RFQ outside the window is excluded - otherwise the filter would do nothing");
    }

    [Fact]
    public async Task A_supplier_gets_nothing_from_the_procurement_dashboard()
    {
        // Worth encoding because the obvious expectation is wrong: a supplier HOLDS rfq.read - it is
        // how they read the RFQs they were invited to - so the permission gate does not stop them.
        // What stops them is having no OrganizationId, and §9.2 makes that a 404 rather than a 403:
        // the answer must not distinguish "you may not" from "there is nothing here".
        var (supplier, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"DashSup {Guid.NewGuid():N}"[..30]);

        var response = await supplier.GetAsync("/api/v1/procurement/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
