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
/// SCR-120 / FR-DSH-008. The supplier's front door.
///
/// <para>Scoped to one SupplierId, which is simpler than the buyer side - and exactly as leaky if
/// done wrong. Every assertion below is on a NUMBER as well as on rows: "Open invitations: 3" that
/// counted another supplier's invitation would disclose volume without naming anything.</para>
/// </summary>
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

    /// <summary>Invites a supplier to a real, published RFQ so the dashboard has something to count.</summary>
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

        // The control: my own invitation is counted and listed.
        myDashboard.GetProperty("kpis").GetProperty("openInvitations").GetInt32().Should().Be(1,
            "control: the supplier's own invitation is counted");
        myDashboard.GetProperty("invitations").EnumerateArray()
            .Select(i => i.GetProperty("rfqReferenceCode").GetString())
            .Should().Contain(myRfq);

        // The count-level negative: two invitations there, one here, and neither number is three.
        theirDashboard.GetProperty("kpis").GetProperty("openInvitations").GetInt32().Should().Be(2,
            "each supplier's count is its own - 3 would disclose the other's volume");

        myDashboard.GetProperty("invitations").EnumerateArray()
            .Select(i => i.GetProperty("rfqReferenceCode").GetString())
            .Should().NotContain(r => r!.Contains("Theirs"), "another supplier's invitations are not visible");
    }

    [Fact]
    public async Task Two_users_of_the_same_supplier_see_the_same_dashboard()
    {
        // §1's personas are supplier_admin AND supplier_user. The scope is the SUPPLIER, not the
        // user, so a colleague sees the same numbers - the control that stops the scope being
        // accidentally per-user.
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
        // §9.2: out of scope reads as not-found rather than forbidden.
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);

        var response = await officer.GetAsync("/api/v1/suppliers/me/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_supplier_who_is_not_yet_approved_is_told_so_rather_than_shown_zeroes()
    {
        // §1's "Not-yet-approved" state is a DIFFERENT SCREEN, not this one with empty widgets. The
        // flag is what lets the client make that distinction - a supplier who is not yet eligible for
        // any invitation must not read "Open invitations: 0" as "nobody wants you".
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(
            fixture, $"DashPending {Guid.NewGuid():N}"[..30]);

        var dashboard = await DashboardAsync(client);

        dashboard.GetProperty("isApproved").GetBoolean().Should().BeFalse();
        dashboard.GetProperty("onboardingState").GetString().Should().NotBe(nameof(SupplierOnboardingState.Approved));
    }

    [Fact]
    public async Task An_approved_supplier_with_no_activity_gets_an_empty_dashboard_not_an_error()
    {
        // §1's "Empty" state: newly approved, nothing yet. Every list empty, every count zero, and a
        // 200 - the difference between "nothing here" and "something went wrong".
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
        // T-001: this comment used to say §12.2's profileCompleteness did not exist and that
        // documents-supplied / documents-total was the only completeness measurable here. Both
        // halves were wrong. The field exists now, and the ratio is the SUBMIT GATE's own checklist -
        // the six profile fields plus the required document types - because BACKLOG.md's T-03.1.1b
        // specifies exactly that: "required sections + mandatory doc types satisfied".
        //
        // requiredDocumentsTotal/Supplied stay DOCUMENT counts; they feed the caption about
        // documents, which is still true of documents.
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
        // The property that makes the definition defensible, and the one the old ratio broke: a
        // supplier reading 100% can submit, and one below it cannot. A meter measuring anything
        // else tells a supplier they are ready when the server disagrees.
        //
        // The old dashboard ratio was documents-only, so a supplier with every required document and
        // no legal information read as 1.0 and was refused at submit. This asserts the two cannot
        // diverge that way again.
        var (client, _) = await ApprovedSupplierAsync($"Agree {Guid.NewGuid():N}"[..30]);

        var health = (await DashboardAsync(client)).GetProperty("profileHealth");
        var completeness = health.GetProperty("completeness").GetDouble();

        // This supplier has uploaded nothing, so it cannot be complete - the control that the
        // assertion below is about a real incomplete profile rather than a vacuous one.
        completeness.Should().BeLessThan(1.0, "nothing has been uploaded yet");

        var submit = await client.PostAsync("/api/v1/suppliers/me/onboarding/submit", null);
        submit.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "a profile the meter reports as incomplete must not pass the gate");
    }

    [Fact]
    public async Task The_profile_response_carries_the_same_number_the_dashboard_shows()
    {
        // §12.2 documents profileCompleteness on the SUPPLIER response, not on the dashboard. Both
        // now come from one evaluator, and this is the assertion that keeps them from drifting into
        // two definitions of one number - which is what T-001 actually was.
        var (client, _) = await ApprovedSupplierAsync($"SameNum {Guid.NewGuid():N}"[..30]);

        var dashboard = (await DashboardAsync(client))
            .GetProperty("profileHealth").GetProperty("completeness").GetDouble();

        var profile = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        profile.GetProperty("profileCompleteness").GetDouble().Should().Be(dashboard);
    }
}
