using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// SCR-300 / FR-DSH-002: "review queue, SLA/aging, pending info-requests, document-expiry watchlist".
///
/// <para><b>Onboarding review has no organization dimension</b>, and the tests say so rather than
/// inventing one: a Supplier onboards onto the platform, not into a buying entity, and has no
/// OrganizationId at all. The boundary that exists here is the permission, so that is what is
/// asserted - in both directions.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReviewDashboardTests(PostgresApiFixture fixture)
{
    private static async Task<JsonElement> DashboardAsync(HttpClient reviewer)
    {
        var response = await reviewer.GetAsync("/api/v1/review/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<string> SubmittedSupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id)
            .ExecuteUpdateAsync(p => p.SetProperty(s => s.OnboardingState, SupplierOnboardingState.Submitted));

        _ = client;
        return supplier.ReferenceCode;
    }

    [Fact]
    public async Task The_counts_move_with_the_queue_they_describe()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var before = await DashboardAsync(reviewer);
        var pendingBefore = before.GetProperty("pending").GetInt32();

        await SubmittedSupplierAsync($"RevDash {Guid.NewGuid():N}"[..30]);

        var after = await DashboardAsync(reviewer);

        // A delta rather than an absolute: the suite shares a database, and asserting "pending == 1"
        // would be asserting that no other test ever submits an application.
        after.GetProperty("pending").GetInt32().Should().Be(pendingBefore + 1,
            "a newly submitted application is exactly one more pending case");
    }

    [Fact]
    public async Task The_dashboard_and_the_queue_agree_about_what_is_open()
    {
        // SCR-300 is presentation over PR #80's queue, so the two must not disagree about the same
        // fact. Computing "open" twice is how they drift; this is the test that catches it.
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        await SubmittedSupplierAsync($"RevAgree {Guid.NewGuid():N}"[..30]);

        var dashboard = await DashboardAsync(reviewer);
        var queue = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/review/queue?withCount=true&state=Submitted");

        var pending = dashboard.GetProperty("pending").GetInt32();
        var queueTotal = queue.GetProperty("pagination").GetProperty("totalCount").GetInt32();

        pending.Should().Be(queueTotal,
            "the KPI and the list it summarises are the same question asked twice");
    }

    [Fact]
    public async Task Aging_is_a_duration_and_never_a_breach()
    {
        // No document defines a review SLA. BUSINESS-PROCESSES §2 says "start review SLA timer",
        // "pause SLA" and "resume SLA timer" and never states a duration; ASM-023's 30 days is
        // document EXPIRY, not review. So the screen reports how long the oldest case has waited and
        // says nothing about whether that is late - a threshold nobody set must not be implied.
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        await SubmittedSupplierAsync($"RevAge {Guid.NewGuid():N}"[..30]);

        var dashboard = await DashboardAsync(reviewer);

        dashboard.TryGetProperty("oldestOpenCaseAgeDays", out var age).Should().BeTrue();
        age.ValueKind.Should().NotBe(JsonValueKind.Undefined);

        // The negative that keeps this honest: no breach flag, no threshold, no "overdue" anywhere in
        // the payload. If someone adds one later, this fails and they have to decide deliberately.
        var body = dashboard.GetRawText().ToLowerInvariant();
        body.Should().NotContain("breach");
        body.Should().NotContain("overdue");
        body.Should().NotContain("sladays");
    }

    [Fact]
    public async Task The_expiry_watchlist_shows_only_documents_a_job_has_already_flagged()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var dashboard = await DashboardAsync(reviewer);

        // Whatever is on the watchlist, every row is in one of the two states the daily job sets.
        // A watchlist that quietly included Approved documents would be a different screen.
        foreach (var row in dashboard.GetProperty("expiryWatchlist").EnumerateArray())
        {
            row.GetProperty("state").GetString()
                .Should().BeOneOf(nameof(DocumentState.ExpiringSoon), nameof(DocumentState.Expired));
        }
    }

    [Fact]
    public async Task Only_a_reviewer_can_read_it()
    {
        // Both directions of the one boundary this screen actually has.
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var (supplier, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"RevDeny {Guid.NewGuid():N}"[..30]);

        (await reviewer.GetAsync("/api/v1/review/dashboard")).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: the persona the screen belongs to can read it");

        (await officer.GetAsync("/api/v1/review/dashboard")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await supplier.GetAsync("/api/v1/review/dashboard")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
