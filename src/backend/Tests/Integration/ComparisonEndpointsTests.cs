using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>FEAT-12.1..12.4/FR-CMP-001..004: real HTTP proof that the comparison matrix never shows
/// a disqualified proposal's pricing and never shows anything evaluation-derived before Consolidated
/// - the same discipline as EvaluationEndpointsTests' own two-envelope/blindness proofs, applied to
/// the cross-proposal read side.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ComparisonEndpointsTests(PostgresApiFixture fixture)
{
    private async Task<(HttpClient Client, Guid SupplierId)> ActiveSupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, Domain.Suppliers.SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, Domain.Suppliers.SupplierLifecycleState.Active));

        return (client, supplier.Id);
    }

    private async Task RunTimelineJobAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<RfqTimelineJob>();
        await job.RunAsync(CancellationToken.None);
    }

    /// <summary>Same shared shape as EvaluationEndpointsTests.SetupEvaluationReadyRfqAsync: two
    /// invited suppliers, both submit, RFQ reaches SubmissionClosed, evaluation opened with a
    /// two-envelope template (Technical threshold 60, Commercial weight 40). Does NOT assign/score/
    /// consolidate - callers do that themselves to land on the exact evaluation state they need to
    /// test against.</summary>
    private async Task<(string RfqReferenceCode, HttpClient Manager, HttpClient Officer, HttpClient SupplierA, HttpClient SupplierB, Guid SupplierAId, Guid SupplierBId, Guid ProposalAId, Guid ProposalBId, Guid TechnicalCriterionId, Guid FinancialCriterionId, Guid OrgId)>
        SetupEvaluationReadyRfqAsync(string titleEn)
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"Cmp {Guid.NewGuid():N}"[..30]);
        var (supplierB, supplierBId) = await ActiveSupplierAsync($"CmpOther {Guid.NewGuid():N}"[..30]);

        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates", new { nameAr = "قالب مقارنة", nameEn = $"Compare Template {Guid.NewGuid():N}" });
        var template = await templateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = template.GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 60, maxScore = 100,
            threshold = 60, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "سعر", nameEn = "Price", dimension = "Commercial", weight = 40, maxScore = 100,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب مقارنة", titleEn, descriptionAr = (string?)null, descriptionEn = (string?)null, currencyCode = "SYP",
            publishAt = (DateTimeOffset?)null, submissionOpensAt = DateTimeOffset.UtcNow.AddSeconds(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddSeconds(3),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString()!;

        var requiredItem = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        var requiredItemBody = await requiredItem.Content.ReadFromJsonAsync<JsonElement>();
        var requiredItemId = requiredItemBody.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        var requirement = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/requirements", new
        {
            textAr = "شرط", textEn = "Mandatory Requirement", isMandatory = true, documentTypeCode = (string?)null,
        });
        var requirementBody = await requirement.Content.ReadFromJsonAsync<JsonElement>();
        var mandatoryRequirementId = requirementBody.GetProperty("requirements").EnumerateArray().Single().GetProperty("id").GetGuid();

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = supplierAId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = supplierBId });
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/approve", null);
        var publish = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await RunTimelineJobAsync();

        async Task<Guid> SubmitProposalAsync(HttpClient supplier)
        {
            var start = await supplier.PostAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/proposal", null);
            var startBody = await start.Content.ReadFromJsonAsync<JsonElement>();
            var proposalReferenceCode = startBody.GetProperty("referenceCode").GetString()!;
            await supplier.PutAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/proposal/items/{requiredItemId}", new
            { quantity = 10m, unitPrice = 5m, discount = (decimal?)null, leadTimeDays = 3, notesAr = (string?)null, notesEn = (string?)null });
            await supplier.PostAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/proposal/requirements/{mandatoryRequirementId}/answer", new { answerAr = "نعم", answerEn = "Yes" });
            await supplier.PutAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/proposal/terms", new
            {
                currencyCode = "SYP", paymentTerms = "Net 30", incotermCode = "FOB", deliveryTermsAr = "3 أيام", deliveryTermsEn = "3 days",
                warranty = (string?)null, validityStart = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
            });
            var submit = await supplier.PostAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/proposal/submit", null);
            submit.StatusCode.Should().Be(HttpStatusCode.OK);

            await using var innerScope = fixture.Services.CreateAsyncScope();
            var innerDb = innerScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var proposal = await innerDb.Proposals.FirstAsync(p => p.ReferenceCode == proposalReferenceCode);
            return proposal.Id;
        }

        var proposalAId = await SubmitProposalAsync(supplierA);
        var proposalBId = await SubmitProposalAsync(supplierB);

        await Task.Delay(TimeSpan.FromSeconds(2));
        await RunTimelineJobAsync();
        var afterClose = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        afterClose.GetProperty("state").GetString().Should().Be(nameof(RfqState.SubmissionClosed));

        var openResult = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/open", null);
        openResult.StatusCode.Should().Be(HttpStatusCode.OK);
        var evaluationBody = await openResult.Content.ReadFromJsonAsync<JsonElement>();
        var criteria = evaluationBody.GetProperty("criteria").EnumerateArray().ToList();
        var technicalCriterionId = criteria.Single(c => !c.GetProperty("isFinancial").GetBoolean()).GetProperty("id").GetGuid();
        var financialCriterionId = criteria.Single(c => c.GetProperty("isFinancial").GetBoolean()).GetProperty("id").GetGuid();

        return (referenceCode, manager, officer, supplierA, supplierB, supplierAId, supplierBId, proposalAId, proposalBId, technicalCriterionId, financialCriterionId, org.Id);
    }

    [Fact]
    public async Task Before_evaluation_is_opened_the_matrix_shows_requirements_but_no_pricing_or_scores()
    {
        var (referenceCode, manager, _, _, _, _, _, _, _, _, _, _) = await SetupEvaluationReadyRfqAsync("Compare RFQ Not Started");

        var response = await manager.GetAsync($"/api/v1/rfqs/{referenceCode}/comparison");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("evaluationState").GetString().Should().Be("NotStarted");
        var proposals = body.GetProperty("proposals").EnumerateArray().ToList();
        proposals.Should().HaveCount(2);
        foreach (var proposal in proposals)
        {
            proposal.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Null);
            proposal.GetProperty("grandTotal").ValueKind.Should().Be(JsonValueKind.Null);
            proposal.GetProperty("technicallyQualified").ValueKind.Should().Be(JsonValueKind.Null);
            proposal.GetProperty("criterionScores").ValueKind.Should().Be(JsonValueKind.Null);
            proposal.GetProperty("requirements").EnumerateArray().Should().ContainSingle(r => r.GetProperty("answered").GetBoolean());
        }
    }

    /// <summary>The blindness half of FEAT-12.4: an evaluator having scored something mid-flight
    /// (InProgress) must not leak into the comparison view for anyone, including the buyer -
    /// BRULE-058's "not readable until Consolidated" applied at the cross-evaluator comparison
    /// level, not just the single-evaluator read path EvaluationEndpointsTests already proves.</summary>
    [Fact]
    public async Task While_evaluation_is_in_progress_the_matrix_still_shows_no_pricing_or_scores_even_after_scoring()
    {
        var (referenceCode, manager, _, _, _, _, _, proposalAId, _, technicalCriterionId, financialCriterionId, _) = await SetupEvaluationReadyRfqAsync("Compare RFQ In Progress");
        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorId } });
        await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = technicalCriterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = financialCriterionId, rawScore = 80m, commentAr = (string?)null, commentEn = (string?)null });

        var response = await manager.GetAsync($"/api/v1/rfqs/{referenceCode}/comparison");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("evaluationState").GetString().Should().Be("InProgress");
        var rawJson = await response.Content.ReadAsStringAsync();
        rawJson.Should().NotContain(evaluatorId.ToString(), "no evaluator identity may appear in a comparison response");
        foreach (var proposal in body.GetProperty("proposals").EnumerateArray())
        {
            proposal.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Null);
            proposal.GetProperty("criterionScores").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Once_consolidated_a_qualified_proposals_pricing_and_scores_show_but_a_disqualified_ones_pricing_stays_absent()
    {
        var (referenceCode, manager, _, _, _, supplierAId, supplierBId, proposalAId, proposalBId, technicalCriterionId, financialCriterionId, _) = await SetupEvaluationReadyRfqAsync("Compare RFQ Consolidated");
        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorId } });
        await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");

        // Proposal A qualifies (90 >= 60 threshold); Proposal B does not (20 < 60).
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = technicalCriterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = financialCriterionId, rawScore = 70m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalBId, criterionId = technicalCriterionId, rawScore = 20m, commentAr = (string?)null, commentEn = (string?)null });
        var submit = await evaluator.PostAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        var consolidate = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/consolidate", null);
        consolidate.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await manager.GetAsync($"/api/v1/rfqs/{referenceCode}/comparison");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("evaluationState").GetString().Should().Be("Consolidated");

        var proposals = body.GetProperty("proposals").EnumerateArray().ToList();
        var proposalA = proposals.Single(p => p.GetProperty("supplierId").GetGuid() == supplierAId);
        var proposalB = proposals.Single(p => p.GetProperty("supplierId").GetGuid() == supplierBId);

        proposalA.GetProperty("technicallyQualified").GetBoolean().Should().BeTrue();
        proposalA.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
        proposalA.GetProperty("items").EnumerateArray().Should().NotBeEmpty();
        proposalA.GetProperty("grandTotal").ValueKind.Should().NotBe(JsonValueKind.Null);
        proposalA.GetProperty("criterionScores").ValueKind.Should().Be(JsonValueKind.Array);

        proposalB.GetProperty("technicallyQualified").GetBoolean().Should().BeFalse();
        proposalB.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Null, "a disqualified proposal's pricing must stay absent even after consolidation");
        proposalB.GetProperty("grandTotal").ValueKind.Should().Be(JsonValueKind.Null);
        proposalB.GetProperty("financialWeightedScore").ValueKind.Should().Be(JsonValueKind.Null);
        // The technical score is still shown for the disqualified proposal - transparency, not a blanket hide.
        proposalB.GetProperty("technicalWeightedScore").ValueKind.Should().NotBe(JsonValueKind.Null);
        proposalB.GetProperty("criterionScores").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task A_staff_user_outside_the_organization_gets_not_found()
    {
        var (referenceCode, _, _, _, _, _, _, _, _, _, _, _) = await SetupEvaluationReadyRfqAsync("Compare RFQ Org Scope");
        var otherOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var outsider = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, otherOrg.Id);

        var response = await outsider.GetAsync($"/api/v1/rfqs/{referenceCode}/comparison");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_role_without_comparison_view_is_refused()
    {
        var (referenceCode, _, _, _, _, _, _, _, _, _, _, orgId) = await SetupEvaluationReadyRfqAsync("Compare RFQ Permission");
        var (evaluator, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, orgId);

        var response = await evaluator.GetAsync($"/api/v1/rfqs/{referenceCode}/comparison");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
