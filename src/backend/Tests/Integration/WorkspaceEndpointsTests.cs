using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Awards;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>FEAT-13.1/FR-PWF-001: real HTTP proof of the guided workspace read model, at the two
/// states report item #1 explicitly calls for - a mid-authoring Draft RFQ (blocked next action,
/// plain-language reason) and a fully-awarded/completed RFQ (system-driven next action, then no
/// next action once the lifecycle is closed).</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class WorkspaceEndpointsTests(PostgresApiFixture fixture)
{
    private async Task RunTimelineJobAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<RfqTimelineJob>();
        await job.RunAsync(CancellationToken.None);
    }

    private async Task RunErpSyncJobAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<AwardErpSyncJob>();
        await job.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Workspace_for_a_Draft_RFQ_shows_Draft_as_current_and_a_blocked_submit_review_action()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب مسودة", titleEn = "Workspace Draft RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null, submissionOpensAt = (DateTimeOffset?)null,
            submissionClosesAt = (DateTimeOffset?)null, clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString()!;

        var workspace = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/workspace");

        workspace.GetProperty("rfqReferenceCode").GetString().Should().Be(referenceCode);
        workspace.GetProperty("rfqState").GetString().Should().Be(nameof(RfqState.Draft));
        workspace.GetProperty("isCancelled").GetBoolean().Should().BeFalse();
        workspace.GetProperty("submittedProposalCount").GetInt32().Should().Be(0);
        workspace.GetProperty("evaluationState").ValueKind.Should().Be(JsonValueKind.Null);
        workspace.GetProperty("awardState").ValueKind.Should().Be(JsonValueKind.Null);

        var stages = workspace.GetProperty("stages").EnumerateArray().ToList();
        stages.Should().HaveCount(10, "only the 10 reachable RfqState values are shown, per the handler's own doc comment");
        var draftStage = stages.Single(s => s.GetProperty("key").GetString() == nameof(RfqState.Draft));
        draftStage.GetProperty("isCurrent").GetBoolean().Should().BeTrue();
        draftStage.GetProperty("isCompleted").GetBoolean().Should().BeFalse();
        var laterStage = stages.Single(s => s.GetProperty("key").GetString() == nameof(RfqState.Published));
        laterStage.GetProperty("isCurrent").GetBoolean().Should().BeFalse();
        laterStage.GetProperty("isCompleted").GetBoolean().Should().BeFalse();

        var actions = workspace.GetProperty("nextActions").EnumerateArray().ToList();
        actions.Should().ContainSingle();
        var submitReview = actions.Single();
        submitReview.GetProperty("action").GetString().Should().Be("submit_review");
        submitReview.GetProperty("permitted").GetBoolean().Should().BeFalse("a Draft RFQ with no items yet cannot be submitted for review");
        submitReview.GetProperty("blockedReasonEn").GetString().Should().Be("No items yet.");
        submitReview.GetProperty("blockedReasonAr").GetString().Should().Be("لا توجد بنود بعد.");
    }

    [Fact]
    public async Task Workspace_for_an_Awarded_RFQ_shows_the_ERP_sync_wait_then_Completed_with_no_next_action()
    {
        var supplierName = $"Wksp {Guid.NewGuid():N}"[..25];
        var (supplierClient, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, supplierName);
        Guid supplierId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == supplierName);
            supplierId = supplier.Id;
            await db.Suppliers.Where(s => s.Id == supplierId).ExecuteUpdateAsync(p => p
                .SetProperty(s => s.OnboardingState, Domain.Suppliers.SupplierOnboardingState.Approved)
                .SetProperty(s => s.LifecycleState, Domain.Suppliers.SupplierLifecycleState.Active));
        }

        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);
        var otherManager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates", new { nameAr = "قالب", nameEn = $"Workspace Template {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب ترسية", titleEn = "Workspace Awarded RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null, currencyCode = "SYP",
            publishAt = (DateTimeOffset?)null, submissionOpensAt = DateTimeOffset.UtcNow.AddSeconds(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddSeconds(3),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var referenceCode = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var itemResponse = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        var itemId = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId });
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/approve", null);
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await RunTimelineJobAsync();

        var start = await supplierClient.PostAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/proposal", null);
        var proposalReferenceCode = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;
        await supplierClient.PutAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/proposal/items/{itemId}", new
        { quantity = 10m, unitPrice = 5m, discount = (decimal?)null, leadTimeDays = 3, notesAr = (string?)null, notesEn = (string?)null });
        await supplierClient.PutAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/proposal/terms", new
        {
            currencyCode = "SYP", paymentTerms = "Net 30", incotermCode = "FOB", deliveryTermsAr = "3 أيام", deliveryTermsEn = "3 days",
            warranty = (string?)null, validityStart = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });
        await supplierClient.PostAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/proposal/submit", null);

        Guid proposalId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            proposalId = (await db.Proposals.FirstAsync(p => p.ReferenceCode == proposalReferenceCode)).Id;
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
        await RunTimelineJobAsync();

        var openResult = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/open", null);
        var criterionId = (await openResult.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("criteria").EnumerateArray().Single().GetProperty("id").GetGuid();

        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorId } });
        await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");
        var scoreResponse = await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId, criterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        scoreResponse.EnsureSuccessStatusCode();
        var submitEval = await evaluator.PostAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/submit", null);
        submitEval.EnsureSuccessStatusCode();
        var consolidate = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/consolidate", null);
        consolidate.EnsureSuccessStatusCode();
        var finalize = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/finalize", null);
        finalize.EnsureSuccessStatusCode();

        var recommend = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/recommend", new
        { winningProposalId = proposalId, justificationAr = "الأفضل", justificationEn = "Best overall" });
        recommend.EnsureSuccessStatusCode();
        var route = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/route-for-approval", null);
        route.EnsureSuccessStatusCode();
        var approve = await otherManager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/approve", null);
        approve.EnsureSuccessStatusCode();
        var execute = await otherManager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/execute", null);
        execute.EnsureSuccessStatusCode();

        var awardedWorkspace = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/workspace");
        awardedWorkspace.GetProperty("rfqState").GetString().Should().Be(nameof(RfqState.Awarded));
        awardedWorkspace.GetProperty("isCancelled").GetBoolean().Should().BeFalse();
        awardedWorkspace.GetProperty("evaluationState").GetString().Should().Be("Finalized");
        awardedWorkspace.GetProperty("awardState").GetString().Should().Be("Awarded");
        var awardedStages = awardedWorkspace.GetProperty("stages").EnumerateArray().ToList();
        awardedStages.Single(s => s.GetProperty("key").GetString() == nameof(RfqState.Awarded)).GetProperty("isCurrent").GetBoolean().Should().BeTrue();
        awardedStages.Single(s => s.GetProperty("key").GetString() == nameof(RfqState.SubmissionClosed)).GetProperty("isCompleted").GetBoolean().Should().BeTrue();
        var awardedActions = awardedWorkspace.GetProperty("nextActions").EnumerateArray().ToList();
        awardedActions.Should().ContainSingle();
        awardedActions.Single().GetProperty("action").GetString().Should().Be("awaiting_erp_sync");
        awardedActions.Single().GetProperty("permitted").GetBoolean().Should().BeFalse("ERP sync is system-driven, no user action can force it");

        await RunErpSyncJobAsync();

        var completedWorkspace = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/workspace");
        completedWorkspace.GetProperty("rfqState").GetString().Should().Be(nameof(RfqState.Completed));
        var completedActions = completedWorkspace.GetProperty("nextActions").EnumerateArray().ToList();
        completedActions.Should().ContainSingle();
        completedActions.Single().GetProperty("action").GetString().Should().Be("completed");
        var completedStages = completedWorkspace.GetProperty("stages").EnumerateArray().ToList();
        completedStages.Single(s => s.GetProperty("key").GetString() == nameof(RfqState.Completed)).GetProperty("isCurrent").GetBoolean().Should().BeTrue();
    }
}
