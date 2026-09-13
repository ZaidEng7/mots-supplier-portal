// SCR-120 and FR-DSH-008. The supplier's front door.
//
// Scoped to one SupplierId, which is simpler than the buyer side - and exactly as leaky if done wrong. Every
// assertion here is on a NUMBER as well as on rows: "Open invitations: 3" that counted another supplier's
// invitation would disclose volume without naming anything. The invite helper invites a supplier to a real,
// published RFQ so the dashboard has something to count.
//
// A supplier sees their own invitations and never another supplier's, with the control that my own invitation
// is counted and listed, and the count-level negative alongside it: two invitations there, one here, and
// neither number is three.
//
// Two users of the same supplier see the same dashboard. §1's personas are supplier_admin AND supplier_user,
// and the scope is the SUPPLIER rather than the user, so a colleague sees the same numbers - the control that
// stops the scope being accidentally per-user.
//
// A staff user has no supplier dashboard: §9.2, out of scope reads as not-found rather than forbidden.
//
// A supplier who is not yet approved is told so rather than shown zeroes. §1's "Not-yet-approved" state is a
// DIFFERENT SCREEN, not this one with empty widgets, and the flag is what lets the client make that
// distinction - a supplier who is not yet eligible for any invitation must not read "Open invitations: 0" as
// "nobody wants you". An approved supplier with no activity gets §1's "Empty" state: every list empty, every
// count zero, and a 200 - the difference between "nothing here" and "something went wrong".
//
// Profile health reports what is missing rather than a number nobody defined. T-001: the note here used to say
// §12.2's profileCompleteness did not exist and that documents-supplied over documents-total was the only
// completeness measurable here. Both halves were wrong. The field exists now, and the ratio is the SUBMIT
// GATE's own checklist - the six profile fields plus the required document types - because BACKLOG.md's
// T-03.1.1b specifies exactly that: "required sections + mandatory doc types satisfied". requiredDocumentsTotal
// and Supplied stay DOCUMENT counts, feeding the caption about documents, which is still true of documents.
//
// The completeness meter and the submit gate agree, which is the property that makes the definition defensible
// and the one the old ratio broke: a supplier reading 100% can submit, and one below it cannot. A meter
// measuring anything else tells a supplier they are ready when the server disagrees. The old dashboard ratio was
// documents-only, so a supplier with every required document and no legal information read as 1.0 and was
// refused at submit. The supplier in that test has uploaded nothing, so it cannot be complete - the control
// that the assertion is about a real incomplete profile rather than a vacuous one.
//
// The profile response carries the same number the dashboard shows. §12.2 documents profileCompleteness on the
// SUPPLIER response rather than on the dashboard; both now come from one evaluator, and this is the assertion
// that keeps them from drifting into two definitions of one number - which is what T-001 actually was.
//
// T-039 and FEAT-16.3: the supplier's own award outcomes reach their dashboard, including the ones they lost.
// The proposals panel excludes NotSelected by design, so before this list a supplier who lost watched their bid
// disappear from the screen they open first, with no outcome on it anywhere. FEAT-16.3's acceptance is "award
// outcomes shown", and a widget carrying only wins would be a scoreboard rather than a record. It is driven
// through the real award chain rather than by writing states into the database, because the question is whether
// an outcome a buyer PRODUCED arrives on the supplier's screen and a test that set ProposalState by hand would
// pass against a build where nothing ever set it. The approval uses a different manager, because BRULE-073
// refuses the recommender as approver, and the figure shown is the supplier's own priced total - the number they
// typed, so no two-envelope question arises.

namespace MotsSupplierPortal.Tests.Integration.Dashboards;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierDashboardTests(PostgresApiFixture fixture)
{
    private async Task<(HttpClient Client, Guid SupplierId)> ApprovedSupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));

        return (client, supplier.Id);
    }

    private static async Task<JsonElement> DashboardAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/suppliers/me/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<string> InviteAsync(Guid supplierId, string label)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"Tpl {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = $"{label} RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(3),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var rfqCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{rfqCode}/evaluation-template", new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/invitations", new { supplierId });
        await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{rfqCode}/approve", null);
        await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/publish", null);

        return rfqCode;
    }

    [Fact]
    public async Task A_supplier_sees_their_own_invitations_and_never_another_suppliers()
    {
        var (mine, mineId) = await ApprovedSupplierAsync($"DashMine {Guid.NewGuid():N}"[..30]);
        var (theirs, theirsId) = await ApprovedSupplierAsync($"DashTheirs {Guid.NewGuid():N}"[..30]);

        var myRfq = await InviteAsync(mineId, "Mine");
        await InviteAsync(theirsId, "Theirs");
        await InviteAsync(theirsId, "TheirsToo");

        var myDashboard = await DashboardAsync(mine);
        var theirDashboard = await DashboardAsync(theirs);

        myDashboard.GetProperty("kpis").GetProperty("openInvitations").GetInt32().Should().Be(1,
            "control: the supplier's own invitation is counted");
        myDashboard.GetProperty("invitations").EnumerateArray()
            .Select(i => i.GetProperty("rfqReferenceCode").GetString())
            .Should().Contain(myRfq);

        theirDashboard.GetProperty("kpis").GetProperty("openInvitations").GetInt32().Should().Be(2,
            "each supplier's count is its own - 3 would disclose the other's volume");

        myDashboard.GetProperty("invitations").EnumerateArray()
            .Select(i => i.GetProperty("rfqReferenceCode").GetString())
            .Should().NotContain(r => r!.Contains("Theirs"), "another supplier's invitations are not visible");
    }

    [Fact]
    public async Task Two_users_of_the_same_supplier_see_the_same_dashboard()
    {
        var (admin, supplierId) = await ApprovedSupplierAsync($"DashTeam {Guid.NewGuid():N}"[..30]);
        await InviteAsync(supplierId, "Team");

        var adminView = await DashboardAsync(admin);

        var colleague = await SupplierTestClient.CreateColleagueAsync(fixture, supplierId);
        var colleagueView = await DashboardAsync(colleague);

        colleagueView.GetProperty("kpis").GetProperty("openInvitations").GetInt32()
            .Should().Be(adminView.GetProperty("kpis").GetProperty("openInvitations").GetInt32(),
                "the dashboard is scoped to the supplier, not to the person reading it");
    }

    [Fact]
    public async Task A_staff_user_has_no_supplier_dashboard()
    {
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);

        var response = await officer.GetAsync("/api/v1/suppliers/me/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_supplier_who_is_not_yet_approved_is_told_so_rather_than_shown_zeroes()
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(
            fixture, $"DashPending {Guid.NewGuid():N}"[..30]);

        var dashboard = await DashboardAsync(client);

        dashboard.GetProperty("isApproved").GetBoolean().Should().BeFalse();
        dashboard.GetProperty("onboardingState").GetString().Should().NotBe(nameof(SupplierOnboardingState.Approved));
    }

    [Fact]
    public async Task An_approved_supplier_with_no_activity_gets_an_empty_dashboard_not_an_error()
    {
        var (client, _) = await ApprovedSupplierAsync($"DashEmpty {Guid.NewGuid():N}"[..30]);

        var dashboard = await DashboardAsync(client);

        dashboard.GetProperty("isApproved").GetBoolean().Should().BeTrue();
        dashboard.GetProperty("kpis").GetProperty("openInvitations").GetInt32().Should().Be(0);
        dashboard.GetProperty("invitations").GetArrayLength().Should().Be(0);
        dashboard.GetProperty("proposals").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Profile_health_reports_what_is_missing_rather_than_a_number_nobody_defined()
    {
        var (client, _) = await ApprovedSupplierAsync($"DashHealth {Guid.NewGuid():N}"[..30]);

        var health = (await DashboardAsync(client)).GetProperty("profileHealth");

        health.GetProperty("completeness").GetDouble().Should().BeInRange(0, 1);
        health.GetProperty("requiredDocumentsTotal").GetInt32().Should().BeGreaterThan(0,
            "the fixture seeds required document types - a zero here would make the ratio meaningless");
        health.GetProperty("requiredDocumentsSupplied").GetInt32().Should().Be(0,
            "this supplier has uploaded nothing yet");
        health.GetProperty("nextRequiredDocumentTypeCode").ValueKind.Should().Be(JsonValueKind.String,
            "§1 asks for the NEXT required document, not just a percentage");
    }

    [Fact]
    public async Task The_completeness_meter_and_the_submit_gate_agree()
    {
        var (client, _) = await ApprovedSupplierAsync($"Agree {Guid.NewGuid():N}"[..30]);

        var health = (await DashboardAsync(client)).GetProperty("profileHealth");
        var completeness = health.GetProperty("completeness").GetDouble();

        completeness.Should().BeLessThan(1.0, "nothing has been uploaded yet");

        var submit = await client.PostAsync("/api/v1/suppliers/me/onboarding/submit", null);
        submit.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "a profile the meter reports as incomplete must not pass the gate");
    }

    [Fact]
    public async Task The_profile_response_carries_the_same_number_the_dashboard_shows()
    {
        var (client, _) = await ApprovedSupplierAsync($"SameNum {Guid.NewGuid():N}"[..30]);

        var dashboard = (await DashboardAsync(client))
            .GetProperty("profileHealth").GetProperty("completeness").GetDouble();

        var profile = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        profile.GetProperty("profileCompleteness").GetDouble().Should().Be(dashboard);
    }

    [Fact]
    public async Task An_award_outcome_reaches_the_suppliers_own_dashboard()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "DashAward");

        await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { seeded.EvaluatorId } });

        Guid criterionId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            criterionId = await db.EvaluationCriterionSnapshots
                .Where(c => c.EvaluationId == seeded.EvaluationId).Select(c => c.Id).FirstAsync();
        }

        await seeded.Evaluator.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/scores",
            new { proposalCode = seeded.ProposalCode, criterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        await seeded.Evaluator.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/submit", null);
        await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/consolidate", null);
        await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/finalize", null);
        await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/recommend", new
        {
            winningProposalCode = seeded.ProposalCode,
            justificationAr = "الأفضل", justificationEn = "Best value",
        });
        await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/route-for-approval", null);

        var approver = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, seeded.OrgId);
        (await approver.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await approver.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/execute", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var dashboard = await seeded.Supplier.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me/dashboard");
        var awards = dashboard.GetProperty("awards").EnumerateArray().ToList();

        var row = awards.Single(a => a.GetProperty("rfqReferenceCode").GetString() == seeded.RfqCode);
        row.GetProperty("outcome").GetString().Should().Be("Awarded");
        row.GetProperty("proposalCode").GetString().Should().Be(seeded.ProposalCode);
        row.GetProperty("value").GetDecimal().Should().BeGreaterThan(0);
    }
}
