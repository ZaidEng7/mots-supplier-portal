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
            var start = await supplier.PostAsync($"/api/v1/rfqs/{referenceCode}/proposals", null);
            var startBody = await start.Content.ReadFromJsonAsync<JsonElement>();
            var proposalReferenceCode = startBody.GetProperty("proposalCode").GetString()!;
            await ProposalPatch.PriceItemAsync(supplier, proposalReferenceCode, requiredItemId, 10m, 5m, (decimal?)null, 3, (string?)null, (string?)null );
            await ProposalPatch.AnswerAsync(supplier, proposalReferenceCode, mandatoryRequirementId, "نعم", "Yes" );
            await ProposalPatch.SetTermsAsync(supplier, proposalReferenceCode, new
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

    // ---------------------------------------------------------------------------------------------
    // FR-CMP-005: the export. EPIC-12 deferred this as priority C and flagged it rather than
    // dropping it.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The export's rows, with the provenance comment lines dropped the way a CSV consumer drops
    /// them, so assertions stay about the DATA.
    /// </summary>
    private static async Task<(List<string> Comments, List<string> Rows, string Raw)> ExportCsvAsync(
        HttpClient client, string referenceCode)
    {
        var response = await client.GetAsync($"/api/v1/rfqs/{referenceCode}/comparison/export?format=csv");
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith(new byte[] { 0xEF, 0xBB, 0xBF }, "Excel reads a BOM-less UTF-8 CSV as the system code page");

        var text = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        return (lines.Where(l => l.StartsWith('#')).ToList(),
                lines.Where(l => !l.StartsWith('#')).ToList(),
                text);
    }

    [Fact]
    public async Task Before_consolidation_the_export_contains_no_price_at_all()
    {
        // The two-envelope property, verified against what the ARTEFACT CONTAINS rather than against
        // the code path that built it. The export shares the screen's handler and issues no query of
        // its own, but that is an argument about the implementation; this is the observation.
        var (referenceCode, manager, _, _, _, _, _, _, _, _, _, _) =
            await SetupEvaluationReadyRfqAsync("Compare Export Blind");

        // What the screen shows: every proposal's pricing absent. The export is checked against this
        // same state rather than against an assumption about it.
        var screen = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/comparison");
        screen.GetProperty("evaluationState").GetString().Should().Be("NotStarted");
        foreach (var proposal in screen.GetProperty("proposals").EnumerateArray())
        {
            proposal.GetProperty("grandTotal").ValueKind.Should().Be(JsonValueKind.Null);
        }

        var (comments, rows, raw) = await ExportCsvAsync(manager, referenceCode);

        // Two proposals, plus the header row.
        rows.Should().HaveCount(3);

        // Absence is rendered as an explicit marker. NOT a zero, not an empty cell, not a dash - any
        // of which a reader could take for a submitted price of nothing.
        rows.Skip(1).Should().OnlyContain(r => r.Contains("(غير متاح بعد)"),
            "a gated value is stated as unavailable, not left blank");

        // And the artefact says WHY the column is empty. Without this line, a comparison exported
        // before consolidation is indistinguishable from one where nobody submitted a price.
        comments.Should().Contain(c => c.Contains("evaluationState") && c.Contains("NotStarted"));

        raw.Should().NotContain("SYP", "no currency, because no priced total is visible yet");
    }

    [Fact]
    public async Task After_consolidation_the_export_shows_exactly_what_the_gate_opened_and_nothing_more()
    {
        // The control for the test above, and the sharper half of the same property: the export must
        // track the gate in BOTH directions. A qualified proposal's total appears; a disqualified
        // one's stays absent in the same file, at the same moment, for the same reader.
        var (referenceCode, manager, _, _, _, supplierAId, supplierBId, proposalAId, proposalBId,
             technicalCriterionId, financialCriterionId, _) =
            await SetupEvaluationReadyRfqAsync("Compare Export Open");

        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorId } });
        await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");

        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = technicalCriterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = financialCriterionId, rawScore = 70m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalBId, criterionId = technicalCriterionId, rawScore = 20m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/submit", null);
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/consolidate", null);

        var screen = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/comparison");
        var proposals = screen.GetProperty("proposals").EnumerateArray().ToList();
        var qualified = proposals.Single(p => p.GetProperty("supplierId").GetGuid() == supplierAId);
        var disqualified = proposals.Single(p => p.GetProperty("supplierId").GetGuid() == supplierBId);

        qualified.GetProperty("grandTotal").ValueKind.Should().NotBe(JsonValueKind.Null,
            "control: the gate really did open for this proposal, so its absence from the file would be a bug");
        disqualified.GetProperty("grandTotal").ValueKind.Should().Be(JsonValueKind.Null,
            "control: and really did not open for this one, so its presence would be the leak");

        var (_, rows, _) = await ExportCsvAsync(manager, referenceCode);

        var qualifiedRow = rows.Single(r => r.Contains(qualified.GetProperty("proposalReferenceCode").GetString()!));
        var disqualifiedRow = rows.Single(r => r.Contains(disqualified.GetProperty("proposalReferenceCode").GetString()!));

        // The total that IS visible appears, in Arabic-Indic digits per R-1, with its currency.
        qualifiedRow.Should().Contain("SYP");
        qualifiedRow.Should().MatchRegex("[٠-٩]", "R-1: currency renders in Arabic-Indic digits under Arabic");
        qualifiedRow.Should().NotContain("(غير متاح بعد)");

        // The one that is not stays the marker - in the same file the other one's price is in.
        disqualifiedRow.Should().Contain("(غير متاح بعد)",
            "the export must not become the path that reintroduces what the screen refuses to show");
        disqualifiedRow.Should().NotContain("SYP");
    }

    [Fact]
    public async Task The_best_value_column_is_marked_with_an_icon_and_a_word_not_by_colour()
    {
        // ACCESSIBILITY.md 1.4.1. On a screen this is usually met with a badge; in a PDF the obvious
        // way to mark a winner is to shade its row, which is invisible to anyone printing in
        // greyscale, reading with low vision, or having the file read aloud. The marker carries a
        // star AND the words, and nothing in the artefact is distinguished by colour at all.
        var (referenceCode, manager, _, _, _, _, _, proposalAId, proposalBId,
             technicalCriterionId, financialCriterionId, _) =
            await SetupEvaluationReadyRfqAsync("Compare Export Best Value");

        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorId } });
        await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = technicalCriterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId = financialCriterionId, rawScore = 70m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalBId, criterionId = technicalCriterionId, rawScore = 20m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/submit", null);
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/consolidate", null);

        var screen = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/comparison");
        var ranked = screen.GetProperty("proposals").EnumerateArray()
            .Where(p => p.GetProperty("rank").ValueKind != JsonValueKind.Null).ToList();
        ranked.Should().NotBeEmpty("control: something has to be ranked first for a best-value marker to exist");

        var winner = ranked.Single(p => p.GetProperty("rank").GetInt32() == 1);
        var (_, rows, _) = await ExportCsvAsync(manager, referenceCode);
        var winnerRow = rows.Single(r => r.Contains(winner.GetProperty("proposalReferenceCode").GetString()!));

        winnerRow.Should().Contain("★", "the icon");
        winnerRow.Should().Contain("أفضل قيمة", "and the words - either one alone fails 1.4.1");

        // Control on the other half: a row that is NOT the winner carries neither, so the assertion
        // above is about rank 1 and not about a marker printed on every row.
        var otherRows = rows.Skip(1).Where(r => r != winnerRow).ToList();
        otherRows.Should().NotBeEmpty();
        otherRows.Should().OnlyContain(r => !r.Contains("★"));
    }

    [Fact]
    public async Task An_unrecognised_export_format_is_refused_rather_than_answered_in_another_one()
    {
        var (referenceCode, manager, _, _, _, _, _, _, _, _, _, _) =
            await SetupEvaluationReadyRfqAsync("Compare Export Format");

        // Controls: both real formats work, so the refusal below is about the value.
        (await manager.GetAsync($"/api/v1/rfqs/{referenceCode}/comparison/export?format=csv"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var pdf = await manager.GetAsync($"/api/v1/rfqs/{referenceCode}/comparison/export?format=pdf");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await pdf.Content.ReadAsByteArrayAsync()).Should().StartWith("%PDF"u8.ToArray());

        var response = await manager.GetAsync($"/api/v1/rfqs/{referenceCode}/comparison/export?format=xlsx");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "silently answering in the default format is the same defect as a silently ignored filter");
    }

    [Fact]
    public async Task A_staff_user_outside_the_organization_cannot_export_either()
    {
        // §9.2: 404, not 403 - and the export is the surface where a scope check is easiest to
        // forget, because the list-level test above does not touch it.
        var (referenceCode, manager, _, _, _, _, _, _, _, _, _, _) =
            await SetupEvaluationReadyRfqAsync("Compare Export Scope");
        var otherOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var outsider = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, otherOrg.Id);

        // The owner control: the same URL, for someone entitled to it, is a 200. Without this the
        // 404 below could be a route that does not exist.
        (await manager.GetAsync($"/api/v1/rfqs/{referenceCode}/comparison/export?format=csv"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await outsider.GetAsync($"/api/v1/rfqs/{referenceCode}/comparison/export?format=csv");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // §9.2's actual property: an RFQ that exists but is out of scope answers IDENTICALLY to one
        // that does not exist, so the status code is the only thing a prober learns and it is the
        // same either way.
        //
        // The first version of this asserted the reference code was absent from the body. It is
        // present - in `instance`, which §7 requires and which echoes the URL the caller typed. That
        // is not a disclosure: the prober supplied the code. Comparing the two responses is the
        // claim that was meant.
        var fabricated = await outsider.GetAsync("/api/v1/rfqs/RFQ-2026-999999/comparison/export?format=csv");
        fabricated.StatusCode.Should().Be(HttpStatusCode.NotFound);

        static string Shape(string body) => System.Text.RegularExpressions.Regex.Replace(
            body, "\"(instance|traceId|correlationId)\":\"[^\"]*\"", "$1");

        Shape(await response.Content.ReadAsStringAsync())
            .Should().Be(Shape(await fabricated.Content.ReadAsStringAsync()),
                "a real RFQ out of scope and an RFQ that never existed must be indistinguishable");
    }
}
