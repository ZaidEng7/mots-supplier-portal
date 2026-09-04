using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>FEAT-08.4/08.6/FR-INV-004/006: the supplier-facing self-service side of Invitations -
/// proves the actual security boundary FEAT-08.6 exists for: a non-invited supplier gets 404 on
/// RFQ detail, enforced server-side, not merely hidden by the frontend.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierRfqEndpointsTests(PostgresApiFixture fixture)
{
    private static object RfqBasics(string titleEn) => new
    {
        titleAr = "طلب اختبار",
        titleEn,
        descriptionAr = (string?)null,
        descriptionEn = (string?)null,
        currencyCode = "SYP",
        publishAt = (DateTimeOffset?)null,
        submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
        submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
        clarificationDeadlineAt = (DateTimeOffset?)null,
        evaluationTargetDate = (DateTimeOffset?)null,
    };

    private async Task<(HttpClient Client, Guid SupplierId)> ActiveSupplierAsync(string name)
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

    /// <summary>Creates, authors, invites <paramref name="invitedSupplierId"/>, submits, approves,
    /// and publishes an RFQ via real HTTP calls - the shared setup every test in this file needs a
    /// Published (supplier-visible) RFQ with exactly one invited supplier.</summary>
    private async Task<string> CreatePublishedRfqWithInviteAsync(Guid invitedSupplierId, string titleEn)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates", new { nameAr = "قالب", nameEn = $"Template {Guid.NewGuid():N}" });
        var template = await templateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = template.GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "معيار", nameEn = "Only Criterion", dimension = "Technical", weight = 100, maxScore = 10,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics(titleEn));
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = invitedSupplierId });
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/approve", null);
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);

        return referenceCode;
    }

    [Fact]
    public async Task A_non_invited_supplier_gets_404_on_rfq_detail()
    {
        var (invitedClient, invitedSupplierId) = await ActiveSupplierAsync($"Invited {Guid.NewGuid():N}"[..30]);
        var referenceCode = await CreatePublishedRfqWithInviteAsync(invitedSupplierId, "Invite-Only RFQ");
        var (outsiderClient, _) = await ActiveSupplierAsync($"Outsider {Guid.NewGuid():N}"[..30]);

        var invitedGet = await invitedClient.GetAsync($"/api/v1/rfqs/{referenceCode}");
        invitedGet.StatusCode.Should().Be(HttpStatusCode.OK, "the actually-invited supplier must see it");

        var outsiderGet = await outsiderClient.GetAsync($"/api/v1/rfqs/{referenceCode}");
        outsiderGet.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-invited supplier must not be able to tell the RFQ exists");

        var wrongReferenceCode = await outsiderClient.GetAsync("/api/v1/rfqs/RFQ-2026-999999");
        wrongReferenceCode.StatusCode.Should().Be(HttpStatusCode.NotFound, "same 404 as a non-existent reference code - no oracle for 'does this RFQ exist'");
    }

    [Fact]
    public async Task A_non_invited_supplier_cannot_decline_an_invitation_that_does_not_exist()
    {
        var (invitedClient, invitedSupplierId) = await ActiveSupplierAsync($"DeclineOwner {Guid.NewGuid():N}"[..30]);
        var referenceCode = await CreatePublishedRfqWithInviteAsync(invitedSupplierId, "Decline Boundary RFQ");
        var (outsiderClient, _) = await ActiveSupplierAsync($"DeclineOutsider {Guid.NewGuid():N}"[..30]);
        _ = invitedClient;

        var declineAttempt = await outsiderClient.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations/decline", new { reason = (string?)null });

        declineAttempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_invited_supplier_sees_the_rfq_in_their_list_and_viewing_detail_marks_it_viewed()
    {
        var (client, supplierId) = await ActiveSupplierAsync($"Lister {Guid.NewGuid():N}"[..30]);
        var referenceCode = await CreatePublishedRfqWithInviteAsync(supplierId, "List And View RFQ");

        // The list returns the §5.2 envelope now, and projects a list item rather than the whole
        // aggregate - invitationStatus is still on it, resolved server-side per caller.
        var list = (await client.GetFromJsonAsync<JsonElement>("/api/v1/rfqs")).GetProperty("data");
        list.EnumerateArray().Should().Contain(r => r.GetProperty("rfqCode").GetString() == referenceCode);
        var listedState = list.EnumerateArray().Single(r => r.GetProperty("rfqCode").GetString() == referenceCode);
        listedState.GetProperty("invitationStatus").GetString().Should().Be(nameof(InvitationStatus.Invited));

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        detail.GetProperty("invitationStatus").GetString().Should().Be(nameof(InvitationStatus.Viewed));
        detail.TryGetProperty("approvals", out _).Should().BeFalse("the supplier-facing shape excludes internal reviewer approvals");
    }

    [Fact]
    public async Task Declining_with_a_reason_is_audited_and_buyer_visible()
    {
        var (client, supplierId) = await ActiveSupplierAsync($"Decliner {Guid.NewGuid():N}"[..30]);
        var referenceCode = await CreatePublishedRfqWithInviteAsync(supplierId, "Decline RFQ");

        var decline = await client.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations/decline", new { reason = "Capacity constraints" });

        decline.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await decline.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("invitationStatus").GetString().Should().Be(nameof(InvitationStatus.Declined));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRow = await db.AuditLogs.SingleAsync(a => a.ReferenceCode == referenceCode && a.Action == "rfq_invitation_declined");
        auditRow.Reason.Should().Be("Capacity constraints");
    }

    [Fact]
    public async Task The_supplier_rfq_list_carries_the_submission_deadline()
    {
        // T-054: §12.4 documents submissionDeadline on this list and it was absent - so the one
        // screen where a supplier decides whether to bid could not show the deadline they would be
        // bidding against.
        var (client, supplierId) = await ActiveSupplierAsync($"Deadline {Guid.NewGuid():N}"[..30]);
        var referenceCode = await CreatePublishedRfqWithInviteAsync(supplierId, "Deadline RFQ");

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/rfqs?pageSize=50");
        var row = body.GetProperty("data").EnumerateArray()
            .Single(r => r.GetProperty("rfqCode").GetString() == referenceCode);

        row.GetProperty("submissionDeadline").ValueKind.Should().NotBe(JsonValueKind.Null,
            "a published RFQ has a close date and the list must show it");

        // Asserted against STORAGE, so this is the RFQ's own deadline rather than any date.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == referenceCode).Select(r => r.SubmissionClosesAt).FirstAsync();

        row.GetProperty("submissionDeadline").GetDateTimeOffset()
            .Should().BeCloseTo(stored!.Value, TimeSpan.FromSeconds(1));
    }
}
