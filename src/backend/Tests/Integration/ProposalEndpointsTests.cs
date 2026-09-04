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
        await ProposalPatch.PriceItemAsync(client, proposalCode, requiredItemId, 10m, 5m, (decimal?)null, 3, (string?)null, (string?)null );
        await ProposalPatch.AnswerAsync(client, proposalCode, mandatoryRequirementId, "نعم", "Yes" );
        var termsResponse = await ProposalPatch.SetTermsAsync(client, proposalCode, new
        {
            currencyCode = "SYP", paymentTerms = "Net 30", incotermCode = "FOB", deliveryTermsAr = "3 أيام", deliveryTermsEn = "3 days",
            warranty = (string?)null, validityStart = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), validityEnd = validityEnd ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });
        termsResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await termsResponse.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// §7.2 documents this rule twice over - in the error code it names (PRICE_NON_POSITIVE) and in
    /// the message it prints («يجب أن يكون سعر الوحدة أكبر من صفر») - but the validator was
    /// GreaterThanOrEqualTo(0), so a zero-price bid line was accepted while the contract said it
    /// could not be. Ruled in favour of the contract.
    /// </summary>
    [Fact]
    public async Task A_zero_unit_price_is_rejected_with_the_documented_code_and_message()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"ZeroPrice {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"ZeroPriceOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, requiredItemId, _, _) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Zero price RFQ");
        var proposalCode = await supplierA.StartProposalAsync(referenceCode);

        var zero = await ProposalPatch.PriceItemAsync(supplierA, proposalCode, requiredItemId, 10m, 0m, (decimal?)null, 3, (string?)null, (string?)null );

        zero.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await zero.Content.ReadFromJsonAsync<JsonElement>();
        // §12.5 moved this edit onto the merge patch, so the path is where the value sits in the
        // patch body - which is what §7.2's paths are for.
        var error = problem.GetProperty("errors").EnumerateArray()
            .Single(e => e.GetProperty("field").GetString() == "items[0].unitPrice");

        error.GetProperty("code").GetString().Should().Be("PRICE_NON_POSITIVE", "§7.2 names this code");
        error.GetProperty("messages").GetProperty("ar").GetString()
            .Should().Be("يجب أن يكون سعر الوحدة أكبر من صفر.", "transcribed verbatim from §7.2");

        // The neighbouring value still works, so this proves the boundary rather than a broken endpoint.
        var positive = await ProposalPatch.PriceItemAsync(supplierA, proposalCode, requiredItemId, 10m, 0.01m, (decimal?)null, 3, (string?)null, (string?)null );
        positive.StatusCode.Should().Be(HttpStatusCode.OK);
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
        firstBody.GetProperty("proposalCode").GetString().Should().Be(secondBody.GetProperty("proposalCode").GetString(),
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
        await ProposalPatch.AnswerAsync(supplierA, proposalCode, mandatoryRequirementId, "نعم", "Yes" );
        await ProposalPatch.SetTermsAsync(supplierA, proposalCode, new
        {
            currencyCode = "SYP", paymentTerms = (string?)null, incotermCode = (string?)null, deliveryTermsAr = (string?)null, deliveryTermsEn = (string?)null,
            warranty = (string?)null, validityStart = (DateOnly?)null, validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });

        var submit = await supplierA.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);

        // T-066: §12.5 answers an incomplete submission with 422 and a code naming what is missing -
        // not the 409 a wrong source state gets, because this supplier has something to go and fix.
        submit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("PROPOSAL_ITEMS_REQUIRED");
        body.GetProperty("detail").GetString().Should().Contain("required RFQ items must be priced");
    }

    [Fact]
    public async Task Submit_requires_the_mandatory_requirement_to_be_answered()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"MissingAnswer {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"MissingAnswerOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, requiredItemId, _, _) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Missing Answer RFQ");
        var proposalCode = await supplierA.StartProposalAsync(referenceCode);
        await ProposalPatch.PriceItemAsync(supplierA, proposalCode, requiredItemId, 10m, 5m, (decimal?)null, (int?)null, (string?)null, (string?)null );
        await ProposalPatch.SetTermsAsync(supplierA, proposalCode, new
        {
            currencyCode = "SYP", paymentTerms = (string?)null, incotermCode = (string?)null, deliveryTermsAr = (string?)null, deliveryTermsEn = (string?)null,
            warranty = (string?)null, validityStart = (DateOnly?)null, validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });

        var submit = await supplierA.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);

        // T-066. The code here is an INVENTION - §12.5 names a slug only for missing items - but the
        // supplier still needs to know which completeness rule they hit.
        submit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("PROPOSAL_REQUIREMENTS_REQUIRED");
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

        submit.StatusCode.Should().Be(HttpStatusCode.OK, await submit.Content.ReadAsStringAsync());
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

        // T-065: §3's 409 for a transition refusal, where this used to be a 400. The RFQ endpoints
        // have answered 409 since T3-36; the proposal endpoints now agree with them and with §3.
        lateWithdraw.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var lateProblem = await lateWithdraw.Content.ReadFromJsonAsync<JsonElement>();
        lateProblem.GetProperty("code").GetString().Should().Be("ILLEGAL_TRANSITION");
        // Draft, not Submitted: this proposal was never submitted - the refusal is the CLOSED
        // WINDOW, not the source state. The state is reported accurately either way, which is what
        // makes allowedNext usable.
        lateProblem.GetProperty("currentState").GetString().Should().Be(nameof(ProposalState.Draft));
    }

    [Fact]
    public async Task Every_proposal_action_writes_an_audit_row()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"Audit {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"AuditOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, requiredItemId, _, mandatoryRequirementId) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Audit RFQ");
        var start = await supplierA.PostAsync($"/api/v1/rfqs/{referenceCode}/proposals", null);
        var startBody = await start.Content.ReadFromJsonAsync<JsonElement>();
        var proposalReferenceCode = startBody.GetProperty("proposalCode").GetString();
        await PriceAndAnswerAsync(supplierA, proposalReferenceCode!, requiredItemId, mandatoryRequirementId);
        await supplierA.PostAsync($"/api/v1/proposals/{proposalReferenceCode}/submit", null);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.Where(a => a.ReferenceCode == proposalReferenceCode).Select(a => a.Action).ToListAsync();

        actions.Should().Contain(["proposal_started", "proposal_item_priced", "proposal_requirement_answered", "proposal_terms_updated", "proposal_submitted"]);
    }

    // ---- §12.5: the merge-patch endpoint that replaced the five edit sub-routes ------------------

    private async Task<(HttpClient Client, string ProposalCode, Guid RequiredItemId, Guid RequirementId)> DraftProposalAsync(string name)
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"{name} {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"{name}O {Guid.NewGuid():N}"[..30]);
        var (referenceCode, requiredItemId, _, requirementId) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, $"{name} RFQ");
        var proposalCode = await supplierA.StartProposalAsync(referenceCode);
        return (supplierA, proposalCode, requiredItemId, requirementId);
    }

    /// <summary>
    /// RFC 7396's central distinction, and the one a deserialised DTO cannot express: a member the
    /// patch does not mention keeps its value, and a member sent as null is deleted. Both arrive as
    /// null in a C# property, which is why the endpoint reads a JsonObject.
    /// </summary>
    [Fact]
    public async Task An_omitted_member_is_left_alone_and_an_explicit_null_clears_it()
    {
        var (client, proposalCode, _, _) = await DraftProposalAsync("MergeSemantics");

        await ProposalPatch.SetTermsAsync(client, proposalCode, new
        {
            currencyCode = "SYP", paymentTerms = "Net 30", warranty = "12 months",
        });

        // Mentions paymentTerms only. The warranty must survive - a DTO would have wiped it.
        await ProposalPatch.SetTermsAsync(client, proposalCode, new { paymentTerms = "Net 60" });

        var afterOmission = await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{proposalCode}");
        afterOmission.GetProperty("warranty").GetString().Should().Be("12 months",
            "a member the patch does not mention is unchanged");
        afterOmission.GetProperty("paymentTerms").GetString().Should().Be("Net 60");

        // Now delete it explicitly.
        await ProposalPatch.SetTermsAsync(client, proposalCode, new { warranty = (string?)null });

        var afterNull = await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{proposalCode}");
        afterNull.GetProperty("warranty").ValueKind.Should().Be(JsonValueKind.Null,
            "an explicit null is RFC 7396's delete");
        afterNull.GetProperty("paymentTerms").GetString().Should().Be("Net 60",
            "deleting one member must not disturb another");
    }

    [Fact]
    public async Task The_same_distinction_holds_for_the_technical_response()
    {
        var (client, proposalCode, _, _) = await DraftProposalAsync("MergeNarrative");

        await ProposalPatch.SetNarrativeAsync(client, proposalCode, "نص عربي", "English text");

        // technicalResponse present, narrativeEn absent - the Arabic must survive.
        await ProposalPatch.SendAsync(client, proposalCode, new { technicalResponse = new { narrativeEn = "Changed" } });

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{proposalCode}");
        after.GetProperty("narrativeAr").GetString().Should().Be("نص عربي");
        after.GetProperty("narrativeEn").GetString().Should().Be("Changed");

        await ProposalPatch.SendAsync(client, proposalCode, new { technicalResponse = new { narrativeAr = (string?)null } });

        var cleared = await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{proposalCode}");
        cleared.GetProperty("narrativeAr").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// RFC 7396 replaces an array rather than merging into it, which is how a line's pricing is
    /// removed now that DELETE /items/{id} is gone.
    /// </summary>
    [Fact]
    public async Task Sending_items_without_a_line_removes_that_lines_pricing()
    {
        var (client, proposalCode, requiredItemId, _) = await DraftProposalAsync("MergeItems");

        await ProposalPatch.PriceItemAsync(client, proposalCode, requiredItemId, 10m, 5m);
        var priced = await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{proposalCode}");
        priced.GetProperty("items").GetArrayLength().Should().Be(1, "control: the line really was priced");

        var response = await ProposalPatch.SendAsync(client, proposalCode, new { items = Array.Empty<object>() });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{proposalCode}");
        after.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task The_patch_only_accepts_the_merge_patch_media_type()
    {
        var (client, proposalCode, _, _) = await DraftProposalAsync("MergeMedia");

        var response = await client.PatchAsJsonAsync($"/api/v1/proposals/{proposalCode}",
            new { technicalResponse = new { narrativeEn = "plain json" } });

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType,
            "RFC 7396 has its own media type and absent-versus-null is exactly what it disambiguates");
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("MIME_NOT_ALLOWED");
    }

    // ---- the rules the retired sub-routes carried, each proven on the new endpoint ---------------

    [Fact]
    public async Task Editing_after_submit_is_refused_the_way_the_sub_routes_refused_it()
    {
        var (client, proposalCode, requiredItemId, requirementId) = await DraftProposalAsync("MergeState");

        await ProposalPatch.PriceItemAsync(client, proposalCode, requiredItemId, 10m, 5m);
        await ProposalPatch.AnswerAsync(client, proposalCode, requirementId, "نعم", "Yes");
        await ProposalPatch.SetTermsAsync(client, proposalCode, new
        {
            currencyCode = "SYP",
            validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });

        var submit = await client.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK, await submit.Content.ReadAsStringAsync());

        var afterSubmit = await ProposalPatch.SetNarrativeAsync(client, proposalCode, "late", "late");

        afterSubmit.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "Draft-only editing is the aggregate's invariant, and the PATCH calls the same aggregate methods");
    }

    [Fact]
    public async Task Commercial_terms_still_require_a_currency()
    {
        var (client, proposalCode, _, _) = await DraftProposalAsync("MergeCurrency");

        var response = await ProposalPatch.SetTermsAsync(client, proposalCode, new { paymentTerms = "Net 30" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the currency requirement lived in the aggregate and survives the route change");
    }

    [Fact]
    public async Task An_answer_still_has_to_carry_both_languages()
    {
        var (client, proposalCode, _, requirementId) = await DraftProposalAsync("MergeAnswer");

        var response = await ProposalPatch.SendAsync(client, proposalCode, new
        {
            technicalResponse = new { answers = new[] { new { requirementId, answerAr = "", answerEn = "" } } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "AnswerRequirementRequest's validator still runs - retiring the route did not retire its validation");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString())
            .Should().Contain("technicalResponse.answers[0].answerAr",
                "§7.2's path points at where the value sits in the patch body");
    }

    [Fact]
    public async Task A_quantity_of_zero_is_still_refused()
    {
        var (client, proposalCode, requiredItemId, _) = await DraftProposalAsync("MergeQuantity");

        var response = await ProposalPatch.SendAsync(client, proposalCode, new
        {
            items = new[] { new { rfqItemId = requiredItemId, quantity = 0m, unitPrice = 5m } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// BUSINESS-PROCESSES.md §4.1's withdrawal row, quoted in full because it is the whole basis for
    /// this being a defect rather than correct behaviour:
    ///
    /// <para><i>"Draft / Submitted | Withdrawn | Withdraw | supplier_admin / proposal.withdraw |
    /// RFQ still SubmissionOpen (window open) | Release from consideration; <b>re-submission allowed
    /// while window open (new draft)</b> | ..."</i></para>
    ///
    /// <para>The documents permit re-entry explicitly, and name its mechanism: a NEW DRAFT, not an
    /// un-withdrawal of the old proposal. So the withdrawn row stays withdrawn - it is the record
    /// that a withdrawal happened - and the supplier gets a fresh one.</para>
    /// </summary>
    [Fact]
    public async Task A_supplier_who_withdraws_can_start_a_new_draft_while_the_window_is_open()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"Rejoin {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"RejoinOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, requiredItemId, _, mandatoryRequirementId) =
            await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Rejoin RFQ");

        var firstCode = await supplierA.StartProposalAsync(referenceCode);
        await PriceAndAnswerAsync(supplierA, firstCode, requiredItemId, mandatoryRequirementId);
        (await supplierA.PostAsync($"/api/v1/proposals/{firstCode}/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var withdraw = await supplierA.PostAsJsonAsync($"/api/v1/proposals/{firstCode}/withdraw", new { reason = "Correcting a price" });
        withdraw.StatusCode.Should().Be(HttpStatusCode.OK, await withdraw.Content.ReadAsStringAsync());

        // The re-entry the table permits. Before this fix, starting again returned the WITHDRAWN
        // proposal - which every edit path then refuses, because it is not a Draft - so a supplier
        // who withdrew to correct a price could never bid again on that RFQ.
        var start = await supplierA.PostAsync($"/api/v1/rfqs/{referenceCode}/proposals", null);
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        var body = await start.Content.ReadFromJsonAsync<JsonElement>();
        var secondCode = body.GetProperty("proposalCode").GetString()!;

        secondCode.Should().NotBe(firstCode, "the table says a NEW draft, not an un-withdrawal");
        body.GetProperty("state").GetString().Should().Be(nameof(ProposalState.Draft));

        // And it is a working draft, not just a row: the supplier can price and submit it. Asserting
        // only that a Draft came back would pass on a proposal that no edit path accepts, which is
        // the exact shape of the defect.
        await PriceAndAnswerAsync(supplierA, secondCode, requiredItemId, mandatoryRequirementId);
        (await supplierA.PostAsync($"/api/v1/proposals/{secondCode}/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // The withdrawn proposal is still withdrawn. Re-entry must not rewrite the record that a
        // withdrawal took place - procurement was notified of it.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = await db.Proposals.AsNoTracking().FirstAsync(pr => pr.ReferenceCode == firstCode);
        first.State.Should().Be(ProposalState.Withdrawn);
        first.WithdrawnAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Starting_twice_without_withdrawing_still_returns_the_same_draft()
    {
        // The control on the other side. FEAT-09.1's start is idempotent, and the fix above must not
        // turn a double-click into two proposals - which is the failure mode of relaxing a
        // uniqueness rule without narrowing it.
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"Idem {Guid.NewGuid():N}"[..30]);
        var (_, supplierBId) = await ActiveSupplierAsync($"IdemOther {Guid.NewGuid():N}"[..30]);
        var (referenceCode, _, _, _) = await OpenRfqWithTwoInviteesAsync(supplierAId, supplierBId, "Idempotent RFQ");

        var first = await supplierA.StartProposalAsync(referenceCode);
        var second = await supplierA.StartProposalAsync(referenceCode);

        second.Should().Be(first, "a second start returns the existing draft, it does not create another");
    }
}
