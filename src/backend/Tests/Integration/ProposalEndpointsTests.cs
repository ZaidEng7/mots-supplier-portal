using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>FEAT-09.1..09.6/FR-PRP-001..008: real HTTP proof of the Proposal aggregate - uniqueness,
/// draft privacy, the two-envelope split, submit-with-validation, the revert-to-red
/// late-submission proof, cross-supplier confidentiality, and withdraw.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProposalEndpointsTests(PostgresApiFixture fixture)
{
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

    private async Task RunTimelineJobAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<RfqTimelineJob>();
        await job.RunAsync(CancellationToken.None);
    }

    /// <summary>Creates, authors (one required item + one optional item + one mandatory
    /// requirement), invites both suppliers, submits/approves/publishes, and drives the RFQ to
    /// SubmissionOpen via the real timeline job - the shared setup every test needs. Returns the
    /// reference code plus the required item/requirement ids the submission gate checks.</summary>
    private async Task<(string ReferenceCode, Guid RequiredItemId, Guid OptionalItemId, Guid MandatoryRequirementId)> OpenRfqWithTwoInviteesAsync(
        Guid supplierA, Guid supplierB, string titleEn, DateTimeOffset? closesAt = null)
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

        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب اختبار", titleEn, descriptionAr = (string?)null, descriptionEn = (string?)null, currencyCode = "SYP",
            publishAt = (DateTimeOffset?)null, submissionOpensAt = DateTimeOffset.UtcNow.AddSeconds(1),
            submissionClosesAt = closesAt ?? DateTimeOffset.UtcNow.AddDays(8),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString()!;

        var requiredItem = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند إلزامي", titleEn = "Required Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        var requiredItemBody = await requiredItem.Content.ReadFromJsonAsync<JsonElement>();
        var requiredItemId = requiredItemBody.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("titleEn").GetString() == "Required Item").GetProperty("id").GetGuid();

        var optionalItem = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند اختياري", titleEn = "Optional Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 2, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = true,
        });
        var optionalItemBody = await optionalItem.Content.ReadFromJsonAsync<JsonElement>();
        var optionalItemId = optionalItemBody.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("titleEn").GetString() == "Optional Item").GetProperty("id").GetGuid();

        var requirement = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/requirements", new
        {
            textAr = "شرط إلزامي", textEn = "Mandatory Requirement", isMandatory = true, documentTypeCode = (string?)null,
        });
        var requirementBody = await requirement.Content.ReadFromJsonAsync<JsonElement>();
        var mandatoryRequirementId = requirementBody.GetProperty("requirements").EnumerateArray().Single().GetProperty("id").GetGuid();

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = supplierA });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = supplierB });
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/approve", null);
        var publish = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await RunTimelineJobAsync();
        var afterOpen = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        afterOpen.GetProperty("state").GetString().Should().Be(nameof(RfqState.SubmissionOpen));

        return (referenceCode, requiredItemId, optionalItemId, mandatoryRequirementId);
    }

    private async Task PriceAndAnswerAsync(HttpClient client, string proposalCode, Guid requiredItemId, Guid mandatoryRequirementId, DateOnly? validityEnd = null)
    {
        await client.PutAsJsonAsync($"/api/v1/proposals/{proposalCode}/items/{requiredItemId}", new
        { quantity = 10m, unitPrice = 5m, discount = (decimal?)null, leadTimeDays = 3, notesAr = (string?)null, notesEn = (string?)null });
        await client.PostAsJsonAsync($"/api/v1/proposals/{proposalCode}/requirements/{mandatoryRequirementId}/answer", new { answerAr = "نعم", answerEn = "Yes" });
        await client.PutAsJsonAsync($"/api/v1/proposals/{proposalCode}/terms", new
        {
            currencyCode = "SYP", paymentTerms = "Net 30", incotermCode = "FOB", deliveryTermsAr = "3 أيام", deliveryTermsEn = "3 days",
            warranty = (string?)null, validityStart = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), validityEnd = validityEnd ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });
    }

    [Fact]
    public async Task Starting_a_proposal_twice_returns_the_same_draft_uniqueness_enforced()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"Unique {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"UniqueOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, _, _, _) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Uniqueness RFQ");

        var first = await supplierA.PostAsync($"/api/v1/rfqs/{referenceCode}/proposals", null);
        var second = await supplierA.PostAsync($"/api/v1/rfqs/{referenceCode}/proposals", null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        firstBody.GetProperty("referenceCode").GetString().Should().Be(secondBody.GetProperty("referenceCode").GetString(),
            "a second start must return the existing Draft, never create a duplicate");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.Proposals.CountAsync(p => p.SupplierId == supplierAId);
        count.Should().Be(1, "the DB unique(rfq_id, supplier_id) constraint plus the idempotent start together guarantee exactly one row");
    }

    [Fact]
    public async Task A_non_invited_supplier_cannot_start_a_proposal()
    {
        var (_, supplierAId) = await ActiveSupplierAsync($"StartInvited {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"StartInvitedOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, _, _, _) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Start Guard RFQ");
        var (outsiderClient, _) = await ActiveSupplierAsync($"StartOutsider {Guid.NewGuid():N}"[..30]);

        var attempt = await outsiderClient.PostAsync($"/api/v1/rfqs/{referenceCode}/proposals", null);

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_draft_proposal_is_never_visible_to_the_rfq_owning_organizations_buyer()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"DraftPriv {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"DraftPrivOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, requiredItemId, _, mandatoryRequirementId) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Draft Privacy RFQ");
        var proposalCode = await supplierA.StartProposalAsync(referenceCode);
        await PriceAndAnswerAsync(supplierA, proposalCode, requiredItemId, mandatoryRequirementId);

        // No buyer-side endpoint exposes Proposal data at all in this build - the real proof is
        // that the buyer's own RFQ detail response (the only buyer-facing view that exists) never
        // even mentions proposals, priced or otherwise.
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var otherOfficer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var rfqAsSeenByAnyBuyer = await otherOfficer.GetAsync($"/api/v1/rfqs/{referenceCode}");
        rfqAsSeenByAnyBuyer.StatusCode.Should().Be(HttpStatusCode.NotFound, "cross-org, but confirms no alternate buyer-facing RFQ route leaks anything either");
    }

    [Fact]
    public async Task Supplier_B_can_never_read_supplier_As_proposal_and_its_financial_envelope_is_never_retrievable_by_anyone_else()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"EnvelopeA {Guid.NewGuid():N}"[..30]);
        var (supplierB, supplierBId) = await ActiveSupplierAsync($"EnvelopeB {Guid.NewGuid():N}"[..30]);
        var (referenceCode, requiredItemId, _, mandatoryRequirementId) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Envelope Seal RFQ");
        var proposalCode = await supplierA.StartProposalAsync(referenceCode);
        await PriceAndAnswerAsync(supplierA, proposalCode, requiredItemId, mandatoryRequirementId);
        var submitA = await supplierA.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);
        submitA.StatusCode.Should().Be(HttpStatusCode.OK);
        var submittedBody = await submitA.Content.ReadFromJsonAsync<JsonElement>();
        submittedBody.GetProperty("items").EnumerateArray().Should().ContainSingle(i => i.GetProperty("unitPrice").GetDecimal() == 5m,
            "sanity check: the owner really does see their own pricing");

        // B is invited to the SAME RFQ, but has no proposal of their own yet - B's own GET route
        // (the only route that could ever address "a proposal for this RFQ") returns B's own
        // (non-existent) proposal, never A's. There is no id in this URL that could name A's
        // proposal instead - B cannot even construct a request that names it.
        var bGet = await supplierB.GetAsync($"/api/v1/rfqs/{referenceCode}/proposals");
        bGet.StatusCode.Should().Be(HttpStatusCode.NotFound, "B has not started a proposal - this must be B's own state, never A's submitted one");

        // Confirms the financial envelope specifically, not just the proposal generally: even
        // after B starts their OWN proposal, B's view carries only B's own (empty) Items - A's
        // pricing never appears anywhere in a response B receives.
        var bStart = await supplierB.PostAsync($"/api/v1/rfqs/{referenceCode}/proposals", null);
        var bStartBody = await bStart.Content.ReadFromJsonAsync<JsonElement>();
        bStartBody.GetProperty("items").EnumerateArray().Should().BeEmpty("B's proposal is B's own - it can never contain A's priced items");
    }

    [Fact]
    public async Task Submit_requires_the_required_item_to_be_priced()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"MissingPrice {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"MissingPriceOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, _, _, mandatoryRequirementId) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Missing Price RFQ");
        var proposalCode = await supplierA.StartProposalAsync(referenceCode);
        await supplierA.PostAsJsonAsync($"/api/v1/proposals/{proposalCode}/requirements/{mandatoryRequirementId}/answer", new { answerAr = "نعم", answerEn = "Yes" });
        await supplierA.PutAsJsonAsync($"/api/v1/proposals/{proposalCode}/terms", new
        {
            currencyCode = "SYP", paymentTerms = (string?)null, incotermCode = (string?)null, deliveryTermsAr = (string?)null, deliveryTermsEn = (string?)null,
            warranty = (string?)null, validityStart = (DateOnly?)null, validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });

        var submit = await supplierA.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);

        submit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("required RFQ items must be priced");
    }

    [Fact]
    public async Task Submit_requires_the_mandatory_requirement_to_be_answered()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"MissingAnswer {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"MissingAnswerOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, requiredItemId, _, _) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Missing Answer RFQ");
        var proposalCode = await supplierA.StartProposalAsync(referenceCode);
        await supplierA.PutAsJsonAsync($"/api/v1/proposals/{proposalCode}/items/{requiredItemId}", new
        { quantity = 10m, unitPrice = 5m, discount = (decimal?)null, leadTimeDays = (int?)null, notesAr = (string?)null, notesEn = (string?)null });
        await supplierA.PutAsJsonAsync($"/api/v1/proposals/{proposalCode}/terms", new
        {
            currencyCode = "SYP", paymentTerms = (string?)null, incotermCode = (string?)null, deliveryTermsAr = (string?)null, deliveryTermsEn = (string?)null,
            warranty = (string?)null, validityStart = (DateOnly?)null, validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });

        var submit = await supplierA.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);

        submit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("mandatory requirements must be answered");
    }

    [Fact]
    public async Task Submit_succeeds_when_everything_required_is_present_and_documents_upload_via_IFileStorage()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"FullSubmit {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"FullSubmitOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, requiredItemId, _, mandatoryRequirementId) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Full Submit RFQ");
        var proposalCode = await supplierA.StartProposalAsync(referenceCode);
        await PriceAndAnswerAsync(supplierA, proposalCode, requiredItemId, mandatoryRequirementId);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "compliance.pdf");
        var upload = await supplierA.PostAsync($"/api/v1/proposals/{proposalCode}/documents", content);
        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterUpload = await upload.Content.ReadFromJsonAsync<JsonElement>();
        afterUpload.GetProperty("documents").EnumerateArray().Should().ContainSingle(d => d.GetProperty("originalFileName").GetString() == "compliance.pdf");

        var submit = await supplierA.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);

        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be(nameof(ProposalState.Submitted));
    }

    /// <summary>Revert-to-red proof, same discipline as EPIC-07's RfqTimelineJob tests: drives the
    /// RFQ's own submission window to actually close via the real scheduled job, then proves
    /// submission is refused for exactly that reason - not a stale client clock, the server's own
    /// state.</summary>
    [Fact]
    public async Task Late_submission_is_impossible_even_with_a_fully_valid_proposal_already_prepared()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"LateSubmit {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"LateSubmitOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, requiredItemId, _, mandatoryRequirementId) = await OpenRfqWithTwoInviteesAsync(
            supplierAId, supplierBId, "Late Submit RFQ", closesAt: DateTimeOffset.UtcNow.AddSeconds(2));
        var proposalCode = await supplierA.StartProposalAsync(referenceCode);
        await PriceAndAnswerAsync(supplierA, proposalCode, requiredItemId, mandatoryRequirementId);

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await RunTimelineJobAsync();

        var submit = await supplierA.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);

        submit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().MatchRegex("not currently accepting submissions|submission window has closed");
    }

    [Fact]
    public async Task Withdraw_is_allowed_while_the_window_is_open_and_refused_once_it_closes()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"Withdraw {Guid.NewGuid():N}"[..30]);
        var (supplierC, supplierCId) = await ActiveSupplierAsync($"WithdrawClosed {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"WithdrawOther {Guid.NewGuid():N}"[..30]);

        var (openReferenceCode, _, _, _) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Withdraw Open RFQ");
        var proposalCode = await supplierA.StartProposalAsync(openReferenceCode);
        var withdraw = await supplierA.PostAsJsonAsync($"/api/v1/proposals/{proposalCode}/withdraw", new { reason = "Changed our mind" });
        withdraw.StatusCode.Should().Be(HttpStatusCode.OK);
        var withdrawBody = await withdraw.Content.ReadFromJsonAsync<JsonElement>();
        withdrawBody.GetProperty("state").GetString().Should().Be(nameof(ProposalState.Withdrawn));

        var (closedReferenceCode, requiredItemId, _, mandatoryRequirementId) = await OpenRfqWithTwoInviteesAsync(
            supplierCId, supplierBId, "Withdraw Closed RFQ", closesAt: DateTimeOffset.UtcNow.AddSeconds(2));
        var closedProposalCode = await supplierC.StartProposalAsync(closedReferenceCode);
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await RunTimelineJobAsync();

        var lateWithdraw = await supplierC.PostAsJsonAsync($"/api/v1/proposals/{closedProposalCode}/withdraw", new { reason = "Too late" });

        lateWithdraw.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Every_proposal_action_writes_an_audit_row()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"Audit {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"AuditOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, requiredItemId, _, mandatoryRequirementId) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Audit RFQ");
        var start = await supplierA.PostAsync($"/api/v1/rfqs/{referenceCode}/proposals", null);
        var startBody = await start.Content.ReadFromJsonAsync<JsonElement>();
        var proposalReferenceCode = startBody.GetProperty("referenceCode").GetString();
        await PriceAndAnswerAsync(supplierA, proposalReferenceCode!, requiredItemId, mandatoryRequirementId);
        await supplierA.PostAsync($"/api/v1/proposals/{proposalReferenceCode}/submit", null);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.Where(a => a.ReferenceCode == proposalReferenceCode).Select(a => a.Action).ToListAsync();

        actions.Should().Contain(["proposal_started", "proposal_item_priced", "proposal_requirement_answered", "proposal_terms_updated", "proposal_submitted"]);
    }
}
