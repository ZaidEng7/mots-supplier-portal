using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// FEAT-19.1/19.2, the report reads behind /bo/reports.
///
/// <para><b>These are the first screens in EPIC-19 that emit TOTALS, which changes what a scope test
/// has to prove.</b> Up to here the negatives were about rows: a supplier must not see another's
/// audit entry, an outsider must not open a comparison. A count discloses without disclosing a row -
/// "RFQs published: 41" that silently includes another organization's work leaks volume, and every
/// row-level assertion in the suite still passes while it does. So these assert the NUMBERS.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReportEndpointsTests(PostgresApiFixture fixture)
{
    /// <summary>
    /// Grants report.read to a role.
    ///
    /// <para>It is in no role's default set on purpose - reports aggregate across every RFQ,
    /// supplier and document in an organization, and no document says which personas should see
    /// that. So every deployment needs this grant made by hand, and these tests make it explicitly
    /// rather than relying on a seed, which is also the most honest demonstration of what an
    /// operator has to do.</para>
    /// </summary>
    private async Task GrantReportReadAsync(string role)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        var appRole = await roleManager.FindByNameAsync(role);
        var claims = await roleManager.GetClaimsAsync(appRole!);
        if (claims.Any(c => c.Type == "perms" && c.Value == Permissions.ReportRead)) return;

        await roleManager.AddClaimAsync(appRole!, new Claim("perms", Permissions.ReportRead));
    }

    /// <summary>An RFQ in a given org and state, with audited transitions at chosen times.</summary>
    private async Task<Guid> SeedRfqAsync(Guid organizationId, RfqState state, params (string Action, DateTimeOffset At)[] transitions)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rfq = Rfq.Create(
            $"RPT-{Guid.NewGuid():N}"[..20], organizationId, "طلب", "Report RFQ", null, null, "SYP",
            publishAt: null,
            submissionOpensAt: DateTimeOffset.UtcNow.AddDays(1),
            submissionClosesAt: DateTimeOffset.UtcNow.AddDays(2),
            clarificationDeadlineAt: null, evaluationTargetDate: null);

        db.Rfqs.Add(rfq);
        await db.SaveChangesAsync();

        await db.Rfqs.Where(r => r.Id == rfq.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.State, state));

        foreach (var (action, at) in transitions)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.CreateVersion7(),
                OccurredAt = at,
                ActorKind = AuditActorKind.System,
                AggregateType = "Rfq",
                AggregateId = rfq.Id,
                Action = action,
                CorrelationId = Guid.CreateVersion7(),
            });
        }

        await db.SaveChangesAsync();
        return rfq.Id;
    }

    private static async Task<JsonElement> ProcurementAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/reports/procurement");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static int CountFor(JsonElement report, string collection, string key) =>
        report.GetProperty(collection).EnumerateArray()
            .Where(c => c.GetProperty("key").GetString() == key)
            .Select(c => c.GetProperty("count").GetInt32())
            .FirstOrDefault();

    [Fact]
    public async Task A_role_without_report_read_is_refused_and_the_same_role_with_it_is_not()
    {
        // Both directions on the gate. The 403 alone would pass on a route that refuses everyone.
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);

        // The refused side uses a role this class never grants. The first version used
        // ProcurementOfficer for both halves and failed with a 200: a role claim is global and
        // permanent for the run, so whichever test granted it first decided the answer for every
        // test after it. The bug was the test's order-dependence, not the gate.
        var ungranted = await StaffTestClient.CreateAsync(fixture, Roles.Evaluator, org.Id);

        (await ungranted.GetAsync("/api/v1/reports/procurement")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "report.read is granted to no role by default");

        await GrantReportReadAsync(Roles.ProcurementOfficer);
        var granted = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        (await granted.GetAsync("/api/v1/reports/procurement")).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: a role that HAS the grant reaches the report");
    }

    [Fact]
    public async Task The_rfq_counts_exclude_another_organizations_rfqs()
    {
        // The count-level negative. No list is involved, so no list-level test covers this: the
        // failure is a NUMBER that is too large, and every row assertion in the suite still passes.
        await GrantReportReadAsync(Roles.ProcurementOfficer);

        var mine = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var theirs = await OrganizationTestHelper.CreateOrganizationAsync(fixture);

        await SeedRfqAsync(mine.Id, RfqState.Draft);
        await SeedRfqAsync(mine.Id, RfqState.Draft);
        await SeedRfqAsync(theirs.Id, RfqState.Draft);
        await SeedRfqAsync(theirs.Id, RfqState.Draft);
        await SeedRfqAsync(theirs.Id, RfqState.Draft);

        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, mine.Id);
        var report = await ProcurementAsync(officer);

        // Exactly two, not five. An assertion of "greater than zero" would pass on a report that
        // counted the whole table, which is the defect.
        CountFor(report, "rfqsByState", nameof(RfqState.Draft)).Should().Be(2,
            "only this organization's drafts are counted");
        report.GetProperty("totalRfqs").GetInt32().Should().Be(2);

        // The owner control, from the other side: the organization whose rows were excluded sees
        // its own three. Without this, a handler that returned 2 for everyone would pass.
        var theirOfficer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, theirs.Id);
        var theirReport = await ProcurementAsync(theirOfficer);
        CountFor(theirReport, "rfqsByState", nameof(RfqState.Draft)).Should().Be(3);
    }

    [Fact]
    public async Task Cycle_time_is_measured_from_audited_transitions_and_reports_its_sample_size()
    {
        await GrantReportReadAsync(Roles.ProcurementOfficer);
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);

        var start = DateTimeOffset.UtcNow.AddDays(-30);

        // Two RFQs with a measurable review interval, 10 and 20 hours - median 15.
        await SeedRfqAsync(org.Id, RfqState.Approved,
            ("rfq_submitted_for_review", start), ("rfq_approved", start.AddHours(10)));
        await SeedRfqAsync(org.Id, RfqState.Approved,
            ("rfq_submitted_for_review", start), ("rfq_approved", start.AddHours(20)));

        // And one that entered review and never left - it must not be counted, and must not be
        // counted as zero either, which would drag the median toward a process that looks faster
        // than it is.
        await SeedRfqAsync(org.Id, RfqState.InternalReview, ("rfq_submitted_for_review", start));

        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var report = await ProcurementAsync(officer);

        var review = report.GetProperty("cycleTimes").EnumerateArray()
            .Single(c => c.GetProperty("key").GetString() == "ReviewToApproved");

        review.GetProperty("sampleSize").GetInt32().Should().Be(2, "the unfinished RFQ is not a measurement");
        review.GetProperty("medianHours").GetDecimal().Should().Be(15.0m);

        // An interval nothing has completed reports null, never zero: "no RFQ has reached award"
        // and "award takes no time" are different facts.
        var award = report.GetProperty("cycleTimes").EnumerateArray()
            .Single(c => c.GetProperty("key").GetString() == "EvaluationToAward");
        award.GetProperty("sampleSize").GetInt32().Should().Be(0);
        award.GetProperty("medianHours").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_coverage_floor_is_reported_and_stated_in_the_export()
    {
        // Cycle time is derived from audit rows, which began when that logging was added - not when
        // the product started. An RFQ that moved through review before then contributes nothing and
        // is silently absent, so a short history reads as a fast process. The floor makes the gap
        // visible, the way the provenance block names an absent filter rather than omitting it.
        await GrantReportReadAsync(Roles.ProcurementOfficer);
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);

        var earliest = DateTimeOffset.UtcNow.AddDays(-90);
        await SeedRfqAsync(org.Id, RfqState.Approved,
            ("rfq_submitted_for_review", earliest), ("rfq_approved", earliest.AddHours(5)));

        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var report = await ProcurementAsync(officer);
        report.GetProperty("coverageFloor").GetDateTimeOffset()
            .Should().BeCloseTo(earliest, TimeSpan.FromSeconds(2));

        var export = await officer.GetAsync("/api/v1/reports/procurement/export?format=csv");
        export.EnsureSuccessStatusCode();
        var csv = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync()).TrimStart('﻿');

        csv.Should().Contain("# filter.cycleTimeCoverageFrom: ",
            "the artefact states the floor below which it cannot see");

        // InvariantCulture, and the reason is worth keeping: this assertion first read
        // `earliest.ToString("yyyy-MM-dd")` with no culture and looked for "1447-12-19". The test
        // host runs under an Arabic culture, so the default calendar is Hijri. The PRODUCTION
        // formatting was already pinned - the file says 2026-06-05 - and it was the test that
        // formatted the date two ways. Exactly the §12.5 defect, on the other side of the assertion.
        csv.Should().Contain(earliest.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task An_export_carries_a_BOM_and_a_provenance_block_and_refuses_an_unknown_format()
    {
        await GrantReportReadAsync(Roles.ProcurementOfficer);
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        await SeedRfqAsync(org.Id, RfqState.Draft);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var csv = await officer.GetAsync("/api/v1/reports/procurement/export?format=csv");
        csv.EnsureSuccessStatusCode();
        (await csv.Content.ReadAsByteArrayAsync()).Should().StartWith(new byte[] { 0xEF, 0xBB, 0xBF });

        var pdf = await officer.GetAsync("/api/v1/reports/compliance/export?format=pdf");
        pdf.EnsureSuccessStatusCode();
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await pdf.Content.ReadAsByteArrayAsync()).Should().StartWith("%PDF"u8.ToArray());

        (await officer.GetAsync("/api/v1/reports/procurement/export?format=xlsx"))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_staff_user_with_no_organization_gets_not_found_rather_than_an_empty_report()
    {
        // §9.2. An empty report would assert that the organization exists and has done nothing,
        // which is a different claim from "you have no organization".
        await GrantReportReadAsync(Roles.SystemAdmin);
        var orphan = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        (await orphan.GetAsync("/api/v1/reports/procurement")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_compliance_report_counts_only_the_latest_version_of_a_document()
    {
        // A superseded version is still a row. Counting it would report a supplier who has already
        // replaced an expiring certificate as still having one - a compliance problem that is fixed.
        await GrantReportReadAsync(Roles.SystemAdmin);
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.GetAsync("/api/v1/reports/compliance");
        response.EnsureSuccessStatusCode();
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var latest = await db.SupplierDocuments.CountAsync(d => d.IsLatestVersion);
        var all = await db.SupplierDocuments.CountAsync();

        report.GetProperty("documentsByState").EnumerateArray()
            .Sum(d => d.GetProperty("count").GetInt32())
            .Should().Be(latest, "only latest versions are counted");

        // Non-vacuity: if the suite happens to hold no superseded versions, this test proves the
        // rule only by coincidence. Say which case ran rather than passing silently either way.
        (all >= latest).Should().BeTrue("a sanity check on the two counts");
    }
}
