using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Awards;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>FEAT-14.1..14.6/FR-AWD-001..007: real HTTP proof of the recommend -&gt; approve ->
/// issue -&gt; ERP-PO flow, with segregation of duties (BRULE-073) and "portal never blocks on ERP"
/// (BRULE-077) as this file's centerpieces, same discipline as EvaluationEndpointsTests'/
/// ComparisonEndpointsTests' own negative-test proofs.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AwardEndpointsTests(PostgresApiFixture fixture)
{
    private sealed class FailingErpPurchaseOrderAdapter : IErpPurchaseOrderAdapter
    {
        public Task<string> CreatePurchaseOrderAsync(Guid awardId, string rfqReferenceCode, CancellationToken ct = default) =>
            throw new InvalidOperationException("ERP is unavailable (simulated for this test).");
    }

    /// <summary>FEAT-13.4 audit: counts real calls FOR ONE SPECIFIC AWARD so a test can prove a job
    /// re-run after a "crash" (here: after the prior run's SaveChangesAsync already committed
    /// ErpSyncStatus.Synced) does NOT call the adapter a second time for that award -
    /// AwardErpSyncJob's own query only ever selects ErpSyncStatus.Requested rows
    /// (AwardErpSyncJob.cs line 33-37), so once a row is committed Synced it structurally cannot be
    /// re-picked-up, the same "idempotent by query construction, not by marker" pattern this epic's
    /// own audit already confirmed for RfqTimelineJob. Scoped to one awardId rather than a global
    /// counter because RunAsync processes its WHOLE pending batch (up to BatchSize) each call - the
    /// shared integration-test database can hold other still-Requested awards left behind by other
    /// tests in this same collection, and a global counter would flake on those, not on anything this
    /// test itself is proving.</summary>
    private sealed class CountingErpPurchaseOrderAdapter(Guid trackedAwardId) : IErpPurchaseOrderAdapter
    {
        public int CallCountForTrackedAward;
        public Task<string> CreatePurchaseOrderAsync(Guid awardId, string rfqReferenceCode, CancellationToken ct = default)
        {
            if (awardId == trackedAwardId) Interlocked.Increment(ref CallCountForTrackedAward);
            return Task.FromResult($"PO-COUNTED-{awardId}");
        }
    }

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

    /// <summary>Carries the RFQ all the way through a Finalized evaluation with two Submitted,
    /// technically-qualified proposals (A scores higher, B lower) - everything AwardEndpointsTests
    /// needs before it can even attempt a recommendation. Mirrors EvaluationEndpointsTests' own
    /// setup helper.</summary>
    private Task<(string RfqReferenceCode, HttpClient Manager, HttpClient Officer, Guid ProposalAId, Guid ProposalBId, Guid SupplierAId, Guid SupplierBId, Guid OrgId)>
        SetupFinalizedEvaluationRfqAsync(string titleEn) => SetupEvaluationRfqAsync(titleEn, finalize: true);

    private async Task<(string RfqReferenceCode, HttpClient Manager, HttpClient Officer, Guid ProposalAId, Guid ProposalBId, Guid SupplierAId, Guid SupplierBId, Guid OrgId)>
        SetupEvaluationRfqAsync(string titleEn, bool finalize)
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"Awd {Guid.NewGuid():N}"[..30]);
        var (supplierB, supplierBId) = await ActiveSupplierAsync($"AwdOther {Guid.NewGuid():N}"[..30]);

        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates", new { nameAr = "قالب ترسية", nameEn = $"Award Template {Guid.NewGuid():N}" });
        var template = await templateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = template.GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب ترسية", titleEn, descriptionAr = (string?)null, descriptionEn = (string?)null, currencyCode = "SYP",
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

        async Task<Guid> SubmitProposalAsync(HttpClient supplier)
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

        var proposalAId = await SubmitProposalAsync(supplierA);
        var proposalBId = await SubmitProposalAsync(supplierB);

        await Task.Delay(TimeSpan.FromSeconds(2));
        await RunTimelineJobAsync();
        var afterClose = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        afterClose.GetProperty("state").GetString().Should().Be(nameof(RfqState.SubmissionClosed));

        var openResult = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/open", null);
        openResult.StatusCode.Should().Be(HttpStatusCode.OK);
        var evaluationBody = await openResult.Content.ReadFromJsonAsync<JsonElement>();
        var criterionId = evaluationBody.GetProperty("criteria").EnumerateArray().Single().GetProperty("id").GetGuid();

        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);
        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorId } });
        await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/my-evaluation");
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalAId, criterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/scores", new
        { proposalId = proposalBId, criterionId, rawScore = 70m, commentAr = (string?)null, commentEn = (string?)null });
        var submitEval = await evaluator.PostAsync($"/api/v1/rfqs/{referenceCode}/my-evaluation/submit", null);
        submitEval.StatusCode.Should().Be(HttpStatusCode.OK);
        var consolidate = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/consolidate", null);
        consolidate.StatusCode.Should().Be(HttpStatusCode.OK);
        if (finalize)
        {
            var finalizeResponse = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/evaluation/finalize", null);
            finalizeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        return (referenceCode, manager, officer, proposalAId, proposalBId, supplierAId, supplierBId, org.Id);
    }

    // ---- Segregation of duties (BRULE-073) - the centerpiece ----

    [Fact]
    public async Task The_recommender_cannot_approve_their_own_recommendation()
    {
        var (referenceCode, manager, _, proposalAId, _, _, _, _) = await SetupFinalizedEvaluationRfqAsync("Award SoD RFQ");

        var recommend = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/recommend", new
        { winningProposalId = proposalAId, justificationAr = "الأفضل فنياً وسعراً", justificationEn = "Best technical and price outcome" });
        recommend.StatusCode.Should().Be(HttpStatusCode.OK);
        var route = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/route-for-approval", null);
        var routeBody = await route.Content.ReadAsStringAsync();
        route.StatusCode.Should().Be(HttpStatusCode.OK, routeBody);

        var selfApprove = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/approve", null);

        selfApprove.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await selfApprove.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("SEGREGATION_OF_DUTIES_VIOLATION");

        var afterAttempt = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}/award");
        afterAttempt.GetProperty("state").GetString().Should().Be("PendingApproval", "a refused self-approval must not change the award's state");
    }

    [Fact]
    public async Task A_different_approver_can_approve_and_reject_requires_a_reason()
    {
        var (referenceCode, manager, _, proposalAId, _, _, _, orgId) = await SetupFinalizedEvaluationRfqAsync("Award Approve RFQ");
        var otherManager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, orgId);

        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/recommend", new
        { winningProposalId = proposalAId, justificationAr = "الأفضل", justificationEn = "Best overall" });
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/route-for-approval", null);

        var approve = await otherManager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK);
        var approveBody = await approve.Content.ReadFromJsonAsync<JsonElement>();
        approveBody.GetProperty("state").GetString().Should().Be("Approved");
    }

    [Fact]
    public async Task Reject_requires_a_reason_and_re_recommend_returns_to_recommended()
    {
        var (referenceCode, manager, _, proposalAId, proposalBId, _, _, orgId) = await SetupFinalizedEvaluationRfqAsync("Award Reject RFQ");
        var otherManager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, orgId);

        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/recommend", new
        { winningProposalId = proposalAId, justificationAr = "الأفضل", justificationEn = "Best overall" });
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/route-for-approval", null);

        var emptyReason = await otherManager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/reject", new { reason = "" });
        emptyReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var reject = await otherManager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/reject", new { reason = "price too high, re-evaluate" });
        reject.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reject.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString().Should().Be("Rejected");

        var reRecommend = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/recommend", new
        { winningProposalId = proposalBId, justificationAr = "بديل", justificationEn = "Alternate winner" });
        reRecommend.StatusCode.Should().Be(HttpStatusCode.OK);
        var reRecommendBody = await reRecommend.Content.ReadFromJsonAsync<JsonElement>();
        reRecommendBody.GetProperty("state").GetString().Should().Be("Recommended");
        reRecommendBody.GetProperty("recommendationRevision").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Recommending_before_the_evaluation_is_finalized_is_refused()
    {
        // Consolidated, deliberately not Finalized - BRULE-071's own gate.
        var (referenceCode, manager, officer, proposalAId, _, _, _, _) = await SetupEvaluationRfqAsync("Award Not Finalized RFQ", finalize: false);

        var recommend = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/recommend", new
        { winningProposalId = proposalAId, justificationAr = "الأفضل", justificationEn = "Best overall" });

        recommend.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await recommend.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("finalized");
    }

    // ---- Issue award: atomic winner/loser update + RFQ transition ----

    [Fact]
    public async Task Executing_the_award_moves_the_winner_to_Awarded_every_other_proposal_to_NotSelected_and_the_RFQ_to_Awarded()
    {
        var (referenceCode, manager, officer, proposalAId, proposalBId, _, _, orgId) = await SetupFinalizedEvaluationRfqAsync("Award Execute RFQ");
        var otherManager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, orgId);

        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/recommend", new
        { winningProposalId = proposalAId, justificationAr = "الأفضل", justificationEn = "Best overall" });
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/route-for-approval", null);
        await otherManager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/approve", null);

        var execute = await otherManager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/execute", null);
        var executeBodyText = await execute.Content.ReadAsStringAsync();

        execute.StatusCode.Should().Be(HttpStatusCode.OK, executeBodyText);
        var executeBody = await execute.Content.ReadFromJsonAsync<JsonElement>();
        executeBody.GetProperty("state").GetString().Should().Be("Awarded");
        executeBody.GetProperty("comparisonSnapshotJson").GetString().Should().NotBeNullOrEmpty("FEAT-14.7: the comparison snapshot must be captured at award time");
        executeBody.GetProperty("erpSyncStatus").GetString().Should().Be("Requested");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var winner = await db.Proposals.FirstAsync(p => p.Id == proposalAId);
        var loser = await db.Proposals.FirstAsync(p => p.Id == proposalBId);
        winner.State.Should().Be(Domain.Proposals.ProposalState.Awarded);
        loser.State.Should().Be(Domain.Proposals.ProposalState.NotSelected);

        var rfq = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        rfq.GetProperty("state").GetString().Should().Be(nameof(RfqState.Awarded));
    }

    // ---- BRULE-077: the portal never blocks on ERP - the other centerpiece ----

    [Fact]
    public async Task The_award_stays_final_and_the_RFQ_stays_Awarded_even_when_the_ERP_adapter_is_down()
    {
        var (referenceCode, manager, officer, proposalAId, _, _, _, orgId) = await SetupFinalizedEvaluationRfqAsync("Award ERP Down RFQ");
        var otherManager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, orgId);

        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/recommend", new
        { winningProposalId = proposalAId, justificationAr = "الأفضل", justificationEn = "Best overall" });
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/route-for-approval", null);
        await otherManager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/approve", null);
        var execute = await otherManager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/execute", null);
        var executeBodyText2 = await execute.Content.ReadAsStringAsync();
        execute.StatusCode.Should().Be(HttpStatusCode.OK, executeBodyText2);
        var awardId = (await execute.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Swap in a failing ERP adapter (IdentityProviderSeamTests' own established pattern) and
        // run the sync job against THAT host - simulating the ERP being down when reconciliation
        // is attempted.
        await using var fakeFactory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddScoped<IErpPurchaseOrderAdapter, FailingErpPurchaseOrderAdapter>()));
        await using (var scope = fakeFactory.Services.CreateAsyncScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<AwardErpSyncJob>();
            await job.RunAsync(CancellationToken.None);
        }

        await using var verifyScope = fixture.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var award = await db.Awards.FirstAsync(a => a.Id == awardId);
        award.State.Should().Be(Domain.Awards.AwardState.Awarded, "AwardState must never regress or roll back because ERP failed");
        award.ErpSyncStatus.Should().Be(Domain.Awards.ErpSyncStatus.Failed);
        award.ErpRetryCount.Should().Be(1);
        award.ExternalPurchaseOrderRef.Should().BeNull();

        var rfq = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        rfq.GetProperty("state").GetString().Should().Be(nameof(RfqState.Awarded), "the RFQ must not advance to Completed while the ERP PO is unacknowledged, but must also not regress");

        // Now prove recovery: the real (stub, always-succeeds) adapter picks the SAME still-Failed
        // award back up on its next run and completes the RFQ.
        await using var recoveryScope = fixture.Services.CreateAsyncScope();
        var recoveryJob = recoveryScope.ServiceProvider.GetRequiredService<AwardErpSyncJob>();
        var recoveryDb = recoveryScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recoveryAward = await recoveryDb.Awards.FirstAsync(a => a.Id == awardId);
        recoveryAward.RetryErpSync();
        await recoveryDb.SaveChangesAsync();
        await recoveryJob.RunAsync(CancellationToken.None);

        var syncedAward = await recoveryDb.Awards.AsNoTracking().FirstAsync(a => a.Id == awardId);
        syncedAward.ErpSyncStatus.Should().Be(Domain.Awards.ErpSyncStatus.Synced);
        syncedAward.ExternalPurchaseOrderRef.Should().NotBeNullOrEmpty();
        var completedRfq = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        completedRfq.GetProperty("state").GetString().Should().Be(nameof(RfqState.Completed));
    }

    // ---- FEAT-13.4/FR-PWF-004: a job re-run after the prior run already committed must be a no-op ----

    [Fact]
    public async Task Rerunning_the_ERP_sync_job_after_a_successful_run_does_not_call_the_adapter_again_or_double_complete_the_RFQ()
    {
        var (referenceCode, manager, officer, proposalAId, _, _, _, orgId) = await SetupFinalizedEvaluationRfqAsync("Award ERP Rerun RFQ");
        var otherManager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, orgId);

        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/recommend", new
        { winningProposalId = proposalAId, justificationAr = "الأفضل", justificationEn = "Best overall" });
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/route-for-approval", null);
        await otherManager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/approve", null);
        var execute = await otherManager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/execute", null);
        var awardId = (await execute.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var countingAdapter = new CountingErpPurchaseOrderAdapter(awardId);
        await using var fakeFactory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddScoped<IErpPurchaseOrderAdapter>(_ => countingAdapter)));

        // First run: the award is still Requested, so this is the ONE real sync. (The batch may
        // also process OTHER awards left Requested by other tests in this shared database - the
        // counter only tracks calls for THIS test's own award, see the adapter's own doc comment.)
        await using (var scope = fakeFactory.Services.CreateAsyncScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<AwardErpSyncJob>();
            await job.RunAsync(CancellationToken.None);
        }
        countingAdapter.CallCountForTrackedAward.Should().Be(1);

        await using var midScope = fixture.Services.CreateAsyncScope();
        var midDb = midScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var midAward = await midDb.Awards.AsNoTracking().FirstAsync(a => a.Id == awardId);
        midAward.ErpSyncStatus.Should().Be(Domain.Awards.ErpSyncStatus.Synced);
        var firstPoRef = midAward.ExternalPurchaseOrderRef;
        firstPoRef.Should().NotBeNullOrEmpty();

        // Simulating the job's recurring schedule firing again (e.g. after a restart) against the
        // SAME already-Synced award: the query in AwardErpSyncJob.RunAsync only selects
        // ErpSyncStatus.Requested rows, so this second run must find nothing to do.
        await using (var scope = fakeFactory.Services.CreateAsyncScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<AwardErpSyncJob>();
            await job.RunAsync(CancellationToken.None);
        }

        countingAdapter.CallCountForTrackedAward.Should().Be(1, "a re-run after the award is already Synced must not call the ERP adapter a second time");

        await using var finalScope = fixture.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var finalAward = await finalDb.Awards.AsNoTracking().FirstAsync(a => a.Id == awardId);
        finalAward.ExternalPurchaseOrderRef.Should().Be(firstPoRef, "the PO reference must not change on a no-op re-run");

        var auditRows = await finalDb.AuditLogs
            .Where(l => l.AggregateType == "Award" && l.AggregateId == awardId && l.Action == "award.erp_po_synced")
            .CountAsync();
        auditRows.Should().Be(1, "a no-op re-run must not write a duplicate audit row either");

        var completedRfq = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        completedRfq.GetProperty("state").GetString().Should().Be(nameof(RfqState.Completed));
    }

    [Fact]
    public async Task Every_award_action_writes_an_audit_row()
    {
        var (referenceCode, manager, _, proposalAId, _, _, _, orgId) = await SetupFinalizedEvaluationRfqAsync("Award Audit RFQ");
        var otherManager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, orgId);

        await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/award/recommend", new
        { winningProposalId = proposalAId, justificationAr = "الأفضل", justificationEn = "Best overall" });
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/route-for-approval", null);
        await otherManager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/approve", null);
        await otherManager.PostAsync($"/api/v1/rfqs/{referenceCode}/award/execute", null);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.Where(a => a.ReferenceCode == referenceCode && a.AggregateType == "Award").Select(a => a.Action).ToListAsync();

        actions.Should().Contain(["award.recommended", "award.pending_approval", "award.approved", "award.awarded"]);
    }
}
