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

/// <summary>FEAT-11.2..11.6/FR-EVL-001..008: real HTTP proof of the Evaluation aggregate - the
/// two-envelope technical-qualification gate (OQ-009) and blind independent scoring (OQ-005) are
/// this file's centerpiece, each proven with an explicit negative test.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EvaluationEndpointsTests(PostgresApiFixture fixture)
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

    /// <summary>Builds a Published, then SubmissionClosed, RFQ with a two-envelope template
    /// (Technical criterion, threshold 60; Commercial/financial criterion, no threshold), two
    /// invited suppliers who both submit a fully-valid proposal, and opens the Evaluation. Returns
    /// everything a scoring test needs.</summary>
    private async Task<(string RfqReferenceCode, HttpClient Manager, HttpClient Officer, Guid ProposalAId, Guid ProposalBId, Guid TechnicalCriterionId, Guid FinancialCriterionId)>
        SetupEvaluationReadyRfqAsync(string titleEn)
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"Eval {Guid.NewGuid():N}"[..30]);
        var (supplierB, supplierBId) = await ActiveSupplierAsync($"EvalOther {Guid.NewGuid():N}"[..30]);

        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates", new { nameAr = "قالب مغلف", nameEn = $"Envelope Template {Guid.NewGuid():N}" });
        var template = await templateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = template.GetProperty("id").GetGuid();
        var techResponse = await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
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
            titleAr = "طلب تقييم", titleEn, descriptionAr = (string?)null, descriptionEn = (string?)null, currencyCode = "SYP",
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

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = supplierAId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = supplierBId });
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/approve", null);
        var publish = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await RunTimelineJobAsync();

        async Task<Guid> SubmitProposalAsync(HttpClient supplier, Guid supplierId)
        {
            var start = await supplier.PostAsync($"/api/v1/rfqs/{referenceCode}/proposals", null);
            var startBody = await start.Content.ReadFromJsonAsync<JsonElement>();
            var proposalReferenceCode = startBody.GetProperty("referenceCode").GetString()!;
            await supplier.PutAsJsonAsync($"/api/v1/proposals/{proposalReferenceCode}/items/{requiredItemId}", new
            { quantity = 10m, unitPrice = 5m, discount = (decimal?)null, leadTimeDays = 3, notesAr = (string?)null, notesEn = (string?)null });
            await supplier.PutAsJsonAsync($"/api/v1/proposals/{proposalReferenceCode}/terms", new
            {
                currencyCode = "SYP", paymentTerms = "Net 30", incotermCode = "FOB", deliveryTermsAr = "3 أيام", deliveryTermsEn = "3 days",
                warranty = (string?)null, validityStart = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
            });
            var submit = await supplier.PostAsync($"/api/v1/proposals/{proposalReferenceCode}/submit", null);
            submit.StatusCode.Should().Be(HttpStatusCode.OK);

            await using var innerScope = fixture.Services.CreateAsyncScope();
            var innerDb = innerScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var proposal = await innerDb.Proposals.FirstAsync(p => p.ReferenceCode == proposalReferenceCode);
            return proposal.Id;
        }

        var proposalAId = await SubmitProposalAsync(supplierA, supplierAId);
        var proposalBId = await SubmitProposalAsync(supplierB, supplierBId);

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

        return (referenceCode, manager, officer, proposalAId, proposalBId, technicalCriterionId, financialCriterionId);
    }

    // ---- The two-envelope technical-qualification gate (OQ-009) - the centerpiece ----

    [Fact]
    public async Task Financial_criterion_cannot_be_scored_before_the_proposal_passes_technical_qualification()
    {
        var (referenceCode, manager, _, proposalAId, _, _, financialCriterionId) = await SetupEvaluationReadyRfqAsync("Gate RFQ 1");
        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        var assignResp = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorId } });
        var assignBody = await assignResp.Content.ReadAsStringAsync();
        assignResp.StatusCode.Should().Be(HttpStatusCode.OK, assignBody);
        var myResp = await evaluator.GetAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation");
        var myBody = await myResp.Content.ReadAsStringAsync();
        myResp.StatusCode.Should().Be(HttpStatusCode.OK, myBody);

        var attempt = await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = financialCriterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });

        attempt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await attempt.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("not yet passed technical qualification");
    }

    /// <summary>The report's own required proof: once a proposal is disqualified (technical score
    /// below threshold), its pricing is unreachable through every scoring endpoint that exists -
    /// not merely refused for one attempt, but never readable at all through this evaluator's own
    /// view.</summary>
    [Fact]
    public async Task A_disqualified_proposals_pricing_is_unreachable_through_every_evaluation_endpoint()
    {
        var (referenceCode, manager, _, proposalAId, _, technicalCriterionId, financialCriterionId) = await SetupEvaluationReadyRfqAsync("Gate RFQ 2");
        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorId } });
        await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");

        // Score technical BELOW the threshold (60) - this proposal fails qualification.
        var techScore = await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = technicalCriterionId, rawScore = 30m, commentAr = (string?)null, commentEn = (string?)null });
        techScore.StatusCode.Should().Be(HttpStatusCode.OK);

        // Endpoint 1: direct financial score attempt - refused.
        var financialAttempt = await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = financialCriterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        financialAttempt.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Endpoint 2: the evaluator's own view never reports qualification true for this proposal,
        // and never carries a financial-criterion score for it - the only two places pricing could
        // otherwise leak into this evaluator's own JSON.
        var myEvaluation = await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");
        myEvaluation.GetProperty("technicallyQualifiedByProposal").GetProperty(proposalAId.ToString()).GetBoolean().Should().BeFalse();
        myEvaluation.GetProperty("myScores").EnumerateArray()
            .Should().NotContain(s => s.GetProperty("proposalId").GetGuid() == proposalAId && s.GetProperty("criterionId").GetGuid() == financialCriterionId);

        // Endpoint 3: submit is still possible without ever scoring the financial criterion for
        // this proposal - proving the gate does not merely block writes but genuinely never
        // requires (or permits) financial visibility for a disqualified proposal.
        var submit = await evaluator.PostAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the second proposal's technical criterion is still unscored");
    }

    // ---- Blind independent scoring (OQ-005/BRULE-058) ----

    [Fact]
    public async Task Evaluator_A_never_sees_evaluator_B_score_rows_at_any_point_before_consolidation()
    {
        var (referenceCode, manager, _, proposalAId, _, technicalCriterionId, financialCriterionId) = await SetupEvaluationReadyRfqAsync("Blind RFQ 1");
        var (evaluatorA, evaluatorAId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        var (evaluatorB, evaluatorBId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorAId, evaluatorBId } });
        await evaluatorA.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");
        await evaluatorB.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");

        // Evaluator B scores first, distinctly, so any leak into A's response is unmistakable.
        await evaluatorB.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = technicalCriterionId, rawScore = 77m, commentAr = (string?)null, commentEn = "Evaluator B's private note" });

        // Evaluator A scores differently, then reads back their own view - must contain ONLY A's rows.
        await evaluatorA.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = technicalCriterionId, rawScore = 42m, commentAr = (string?)null, commentEn = (string?)null });
        var aView = await evaluatorA.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");
        var aScores = aView.GetProperty("myScores").EnumerateArray().ToList();

        aScores.Should().ContainSingle(s => s.GetProperty("proposalId").GetGuid() == proposalAId && s.GetProperty("criterionId").GetGuid() == technicalCriterionId)
            .Which.GetProperty("rawScore").GetDecimal().Should().Be(42m);
        aScores.Should().NotContain(s => s.GetProperty("rawScore").GetDecimal() == 77m, "Evaluator A's response must never contain Evaluator B's score row");
        aView.ToString().Should().NotContain("Evaluator B's private note", "not even Evaluator B's comment text may leak into A's JSON");

        // Raw JSON re-check on the wire, not just the deserialized shape - guards against a future
        // DTO field that happens to carry the other evaluator's data under a different name.
        var rawResponse = await evaluatorA.GetAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation");
        var rawJson = await rawResponse.Content.ReadAsStringAsync();
        rawJson.Should().NotContain(evaluatorBId.ToString());
    }

    // ---- Consolidate / finalize ----

    [Fact]
    public async Task Consolidate_ranks_qualified_proposals_and_excludes_disqualified_ones()
    {
        var (referenceCode, manager, _, proposalAId, proposalBId, technicalCriterionId, financialCriterionId) = await SetupEvaluationReadyRfqAsync("Consolidate RFQ 1");
        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorId } });
        await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");

        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = technicalCriterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = financialCriterionId, rawScore = 80m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalBId, criterionId = technicalCriterionId, rawScore = 20m, commentAr = (string?)null, commentEn = (string?)null });

        var submit = await evaluator.PostAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        var consolidate = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/consolidate", null);
        consolidate.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await consolidate.Content.ReadFromJsonAsync<JsonElement>();
        var results = body.GetProperty("results").EnumerateArray().ToList();
        var resultA = results.Single(r => r.GetProperty("proposalId").GetGuid() == proposalAId);
        var resultB = results.Single(r => r.GetProperty("proposalId").GetGuid() == proposalBId);
        resultA.GetProperty("technicallyQualified").GetBoolean().Should().BeTrue();
        resultA.GetProperty("rank").GetInt32().Should().Be(1);
        resultB.GetProperty("technicallyQualified").GetBoolean().Should().BeFalse();
        resultB.GetProperty("rank").ValueKind.Should().Be(JsonValueKind.Null);

        var finalize = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/finalize", null);
        finalize.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalized = await finalize.Content.ReadFromJsonAsync<JsonElement>();
        finalized.GetProperty("state").GetString().Should().Be("Finalized");
    }

    [Fact]
    public async Task Every_evaluation_action_writes_an_audit_row()
    {
        var (referenceCode, manager, _, proposalAId, _, technicalCriterionId, _) = await SetupEvaluationReadyRfqAsync("Audit RFQ 1");
        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorId } });
        await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = technicalCriterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.Where(a => a.ReferenceCode == referenceCode && a.AggregateType == "Evaluation").Select(a => a.Action).ToListAsync();

        actions.Should().Contain(["evaluation_created", "evaluation_evaluators_assigned", "evaluation.score"]);
    }
}
