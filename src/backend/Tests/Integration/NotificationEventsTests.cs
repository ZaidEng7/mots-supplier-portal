using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// EPIC-15 Phase 3: the notifications BUSINESS-PROCESSES.md's transition tables document and which
/// fired nothing before this epic.
///
/// <para>Each assertion checks three things, because each can be wrong independently: that a
/// notification was produced at all, that it reached the recipient the TABLE names (not the one that
/// was convenient to resolve), and that it was produced ONCE - a transition that notifies twice is
/// as wrong as one that notifies nobody, and only a count catches it.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class NotificationEventsTests(PostgresApiFixture fixture)
{
    private static async Task AssertNotifiedOnceAsync(PostgresApiFixture fixture, Guid recipient, string type)
    {
        var rows = await NotificationTestHelper.ForRecipientAsync(fixture, recipient, type);

        rows.Should().ContainSingle($"'{type}' must reach this recipient exactly once");
        rows[0].TitleAr.Should().NotBeNullOrWhiteSpace();
    }

    private static async Task AssertNotNotifiedAsync(PostgresApiFixture fixture, Guid recipient, string type)
    {
        var rows = await NotificationTestHelper.ForRecipientAsync(fixture, recipient, type);
        rows.Should().BeEmpty($"'{type}' is not addressed to this persona by the transition table");
    }

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

    private static object RfqBasics(string titleEn, DateTimeOffset? opensAt = null, DateTimeOffset? closesAt = null) => new
    {
        titleAr = "طلب اختبار", titleEn,
        descriptionAr = (string?)null, descriptionEn = (string?)null,
        currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
        submissionOpensAt = opensAt ?? DateTimeOffset.UtcNow.AddDays(1),
        submissionClosesAt = closesAt ?? DateTimeOffset.UtcNow.AddDays(8),
        clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
    };

    private static async Task<Guid> CreateActiveTemplateAsync(HttpClient manager)
    {
        var response = await manager.PostAsJsonAsync("/api/v1/evaluation-templates", new { nameAr = "قالب", nameEn = $"Template {Guid.NewGuid():N}" });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{id}/criteria", new
        {
            nameAr = "معيار", nameEn = "Only Criterion", dimension = "Technical", weight = 100, maxScore = 10,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{id}/activate", null);
        return id;
    }

    private sealed record Lifecycle(
        HttpClient Officer, Guid OfficerId, HttpClient Manager, Guid ManagerId,
        HttpClient Supplier, Guid SupplierUserId, Guid OrgId, string RfqCode);

    /// <summary>An RFQ in InternalReview, with one item, one invitee and a bound template.</summary>
    private async Task<Lifecycle> RfqInReviewAsync(string label, DateTimeOffset? opensAt = null, DateTimeOffset? closesAt = null)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, officerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (manager, managerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementManager, org.Id);
        var templateId = await CreateActiveTemplateAsync(manager);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics($"{label} RFQ", opensAt, closesAt));
        var rfqCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{rfqCode}/evaluation-template", new { evaluationTemplateId = templateId });

        var (supplier, supplierId) = await ActiveSupplierAsync($"{label} Sup {Guid.NewGuid():N}"[..30]);
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/invitations", new { supplierId });

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplierUserId = await db.Users.Where(u => u.SupplierId == supplierId).Select(u => u.Id).FirstAsync();

        await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/submit-review", null);

        return new Lifecycle(officer, officerId, manager, managerId, supplier, supplierUserId, org.Id, rfqCode);
    }

    // ---- RFQ (§3.1) ----------------------------------------------------------------------------

    [Fact]
    public async Task Submitting_an_RFQ_for_review_notifies_the_procurement_manager_and_not_the_supplier()
    {
        var life = await RfqInReviewAsync("SubmitReview");

        // §3.1 "Draft -> InternalReview | In-app to `procurement_manager`".
        await AssertNotifiedOnceAsync(fixture, life.ManagerId, NotificationTypes.RfqSubmittedForReview);

        // The negative, with the control above: an internal review step is not the supplier's
        // business, and a notification centre is exactly where that would leak.
        await AssertNotNotifiedAsync(fixture, life.SupplierUserId, NotificationTypes.RfqSubmittedForReview);
    }

    [Fact]
    public async Task Returning_an_RFQ_for_edits_notifies_the_officer()
    {
        var life = await RfqInReviewAsync("Returned");

        await life.Manager.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/return", new { comments = "Please add pricing detail" });

        // §3.1 "InternalReview -> Draft | In-app to officer".
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.RfqReturnedForEdits);
    }

    [Fact]
    public async Task Approving_an_RFQ_notifies_the_officer()
    {
        var life = await RfqInReviewAsync("Approved");

        await life.Manager.PostAsync($"/api/v1/rfqs/{life.RfqCode}/approve", null);

        // §3.1 "InternalReview -> Approved | In-app to officer".
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.RfqApproved);
    }

    [Fact]
    public async Task Opening_and_closing_the_submission_window_notifies_the_invitees()
    {
        // Clock-driven transitions, but still state changes - so the notification travels the same
        // Outbox in the same transaction (D-5).
        var life = await RfqInReviewAsync("Window");

        await life.Manager.PostAsync($"/api/v1/rfqs/{life.RfqCode}/approve", null);
        await life.Officer.PostAsync($"/api/v1/rfqs/{life.RfqCode}/publish", null);

        // The window has to be in the future to be SET - deadlines are validated as future on
        // creation - and in the past for the job to act on it. Moved in the database rather than by
        // waiting, which is the same thing every other timeline test in this suite does.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Rfqs.Where(r => r.ReferenceCode == life.RfqCode).ExecuteUpdateAsync(p => p
                .SetProperty(r => r.SubmissionOpensAt, DateTimeOffset.UtcNow.AddMinutes(-10))
                .SetProperty(r => r.SubmissionClosesAt, DateTimeOffset.UtcNow.AddMinutes(-5)));
        }

        // Twice: one run opens the window, and the close query was evaluated against the state as it
        // was before that. Production reaches the same place on the next scheduled tick.
        for (var run = 0; run < 2; run++)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
        }

        // §3.1 "Published -> SubmissionOpen | In-app to invitees" and
        //      "SubmissionOpen -> SubmissionClosed | In-app to invitees + committee".
        await AssertNotifiedOnceAsync(fixture, life.SupplierUserId, NotificationTypes.RfqSubmissionOpened);
        await AssertNotifiedOnceAsync(fixture, life.SupplierUserId, NotificationTypes.RfqSubmissionClosed);
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.RfqSubmissionClosed);

        // The committee is told about the CLOSE, not the open - the table says invitees for one and
        // invitees plus committee for the other, and the difference is deliberate.
        await AssertNotNotifiedAsync(fixture, life.OfficerId, NotificationTypes.RfqSubmissionOpened);
    }

    // ---- Evaluation and award (§3.3, §3.4) ------------------------------------------------------

    private sealed record AwardLifecycle(
        HttpClient Officer, Guid OfficerId, HttpClient Manager, Guid ManagerId,
        HttpClient Evaluator, Guid EvaluatorId, HttpClient SupplierA, string RfqCode,
        Guid WinningProposalId, string WinningProposalCode, Guid SupplierUserId);

    /// <summary>An RFQ whose evaluation is open, scored and (optionally) consolidated.</summary>
    private async Task<AwardLifecycle> EvaluatedRfqAsync(
        string label, bool consolidate = true, bool finalize = true,
        bool submitScores = true, bool closeWindow = true)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, officerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (manager, managerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementManager, org.Id);
        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates", new { nameAr = "قالب", nameEn = $"Tpl {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        // A window that closes in three seconds when the test needs an evaluation, and one that
        // stays open when the test is about something a supplier does while it is open - withdrawal
        // is refused once the window closes, which is the rule and not an obstacle to route around.
        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics($"{label} RFQ",
            DateTimeOffset.UtcNow.AddSeconds(1),
            closeWindow ? DateTimeOffset.UtcNow.AddSeconds(3) : DateTimeOffset.UtcNow.AddHours(4)));
        var rfqCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var itemResponse = await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        var itemId = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{rfqCode}/evaluation-template", new { evaluationTemplateId = templateId });

        var (supplierA, supplierAId) = await ActiveSupplierAsync($"{label}A {Guid.NewGuid():N}"[..30]);
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/invitations", new { supplierId = supplierAId });
        await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{rfqCode}/approve", null);
        await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/publish", null);

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
        }

        var start = await supplierA.PostAsync($"/api/v1/rfqs/{rfqCode}/proposals", null);
        var proposalCode = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("proposalCode").GetString()!;
        await ProposalPatch.PriceItemAsync(supplierA, proposalCode, itemId, 10m, 5m);
        await ProposalPatch.SetTermsAsync(supplierA, proposalCode, new
        {
            currencyCode = "SYP", paymentTerms = "Net 30", incotermCode = "FOB",
            validityStart = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date),
            validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });
        var proposalSubmit = await supplierA.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);
        proposalSubmit.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await proposalSubmit.Content.ReadAsStringAsync());

        if (!closeWindow)
        {
            // Nothing further to set up: the caller wants the proposal submitted with the window
            // still open.
            await using var openScope = fixture.Services.CreateAsyncScope();
            var openDb = openScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var openProposalId = await openDb.Proposals.Where(p => p.ReferenceCode == proposalCode).Select(p => p.Id).FirstAsync();
            var openSupplierUserId = await openDb.Users.Where(u => u.SupplierId == supplierAId).Select(u => u.Id).FirstAsync();

            return new AwardLifecycle(officer, officerId, manager, managerId, evaluator, evaluatorId,
                supplierA, rfqCode, openProposalId, proposalCode, openSupplierUserId);
        }

        await Task.Delay(TimeSpan.FromSeconds(2.2));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
        }

        var opened = await manager.PostAsync($"/api/v1/rfqs/{rfqCode}/evaluation/open", null);
        opened.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await opened.Content.ReadAsStringAsync());
        await manager.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/evaluation/assignments", new { evaluatorUserIds = new[] { evaluatorId } });

        Guid proposalId;
        Guid supplierUserId;
        List<Guid> criterionIds;
        Guid evaluationId;

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            proposalId = await db.Proposals.Where(p => p.ReferenceCode == proposalCode).Select(p => p.Id).FirstAsync();
            supplierUserId = await db.Users.Where(u => u.SupplierId == supplierAId).Select(u => u.Id).FirstAsync();
            evaluationId = await db.Evaluations
                .Where(e => db.Rfqs.Any(r => r.Id == e.RfqId && r.ReferenceCode == rfqCode))
                .Select(e => e.Id).FirstAsync();
            criterionIds = await db.EvaluationCriterionSnapshots
                .Where(c => c.EvaluationId == evaluationId)
                .Select(c => c.Id).ToListAsync();
        }

        // EVERY criterion of EVERY proposal - submitting scores is refused until they are all in,
        // which is the same rule the "all evaluators submitted" notification depends on.
        foreach (var criterionId in criterionIds)
        {
            var scored = await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/my-evaluation/scores",
                new { proposalCode = await fixture.ProposalCodeAsync(proposalId), criterionId, rawScore = 80m, commentAr = (string?)null, commentEn = (string?)null });
            scored.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await scored.Content.ReadAsStringAsync());
        }

        // Asserted, not fired and forgotten: a helper that silently fails here produces tests that
        // fail much later with "no notification", which says nothing about why.
        if (submitScores)
        {
            var submitted = await evaluator.PostAsync($"/api/v1/rfqs/{rfqCode}/my-evaluation/submit", null);
            submitted.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync());
        }

        if (consolidate)
        {
            var consolidated = await manager.PostAsync($"/api/v1/rfqs/{rfqCode}/evaluation/consolidate", null);
            consolidated.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await consolidated.Content.ReadAsStringAsync());
        }

        if (finalize)
        {
            var finalized = await manager.PostAsync($"/api/v1/rfqs/{rfqCode}/evaluation/finalize", null);
            finalized.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await finalized.Content.ReadAsStringAsync());
        }

        return new AwardLifecycle(officer, officerId, manager, managerId, evaluator, evaluatorId,
            supplierA, rfqCode, proposalId, proposalCode, supplierUserId);
    }

    [Fact]
    public async Task The_evaluation_transitions_notify_the_groups_the_table_names()
    {
        var life = await EvaluatedRfqAsync("EvalFlow");

        // §3.3, in order: opened (committee), all evaluators in (officer), consolidated (committee),
        // finalized (committee).
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.EvaluationOpened);
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.EvaluatorSubmitted);
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.EvaluationConsolidated);
        await AssertNotifiedOnceAsync(fixture, life.ManagerId, NotificationTypes.EvaluationFinalized);

        // The supplier is not on the committee, and an evaluation centre is exactly where that would
        // leak - the control above is the four assertions that DID land.
        await AssertNotNotifiedAsync(fixture, life.SupplierUserId, NotificationTypes.EvaluationConsolidated);
    }

    [Fact]
    public async Task Reopening_an_evaluation_notifies_the_assigned_evaluators()
    {
        var life = await EvaluatedRfqAsync("EvalReopen", finalize: false);

        await life.Manager.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/evaluation/reopen", new { reason = "Recount needed" });

        // §3.3 "Consolidated -> InProgress | In-app to affected evaluators".
        await AssertNotifiedOnceAsync(fixture, life.EvaluatorId, NotificationTypes.EvaluationReopened);
    }

    [Fact]
    public async Task Recusing_an_evaluator_notifies_the_officer()
    {
        var life = await EvaluatedRfqAsync("EvalRecuse", consolidate: false, finalize: false, submitScores: false);

        var recused = await life.Manager.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/evaluation/recuse",
            new { evaluatorUserId = life.EvaluatorId, reason = "Conflict of interest" });
        recused.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await recused.Content.ReadAsStringAsync());

        // §3.3 has no row for recusal - an invention, flagged in the catalogue and in the report.
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.EvaluatorRecused);
    }

    [Fact]
    public async Task The_award_transitions_notify_the_approver_pool_then_the_officer()
    {
        var life = await EvaluatedRfqAsync("AwardFlow");

        await life.Officer.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/award/recommend", new
        {
            winningProposalId = life.WinningProposalId,
            justificationAr = "الأفضل سعراً", justificationEn = "Best value",
        });
        await life.Officer.PostAsync($"/api/v1/rfqs/{life.RfqCode}/award/route-for-approval", null);
        await life.Manager.PostAsync($"/api/v1/rfqs/{life.RfqCode}/award/approve", null);

        // §3.4: recommended and routed go to the approver(s); approved comes back to the officer.
        await AssertNotifiedOnceAsync(fixture, life.ManagerId, NotificationTypes.AwardRecommended);
        await AssertNotifiedOnceAsync(fixture, life.ManagerId, NotificationTypes.AwardRoutedForApproval);
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.AwardApproved);

        // A supplier is never told about an award decision before it is executed - the regret and
        // award notifications are §3.4's LAST transition, not this one.
        await AssertNotNotifiedAsync(fixture, life.SupplierUserId, NotificationTypes.AwardApproved);
    }

    [Fact]
    public async Task Rejecting_an_award_notifies_the_officer_and_re_recommending_notifies_the_approver_again()
    {
        var life = await EvaluatedRfqAsync("AwardReject");

        await life.Officer.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/award/recommend", new
        {
            winningProposalId = life.WinningProposalId,
            justificationAr = "الأفضل", justificationEn = "Best",
        });
        await life.Officer.PostAsync($"/api/v1/rfqs/{life.RfqCode}/award/route-for-approval", null);
        await life.Manager.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/award/reject", new { reason = "Insufficient justification" });

        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.AwardRejected);

        await life.Officer.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/award/recommend", new
        {
            winningProposalId = life.WinningProposalId,
            justificationAr = "مبرر أوفى", justificationEn = "Fuller justification",
        });

        // §3.4 "Rejected -> Recommended | In-app to approver" - a distinct type from the first
        // recommendation, because the approver needs to know this one follows their own rejection.
        await AssertNotifiedOnceAsync(fixture, life.ManagerId, NotificationTypes.AwardReRecommended);
    }

    [Fact]
    public async Task Withdrawing_a_proposal_notifies_the_supplier_and_the_committee()
    {
        var life = await EvaluatedRfqAsync("Withdraw", consolidate: false, finalize: false, closeWindow: false);

        await life.SupplierA.PostAsJsonAsync($"/api/v1/proposals/{life.WinningProposalCode}/withdraw",
            new { reason = "Cannot supply in time" });

        // §3.2 "Draft / Submitted -> Withdrawn | In-app to supplier + procurement" - both groups.
        await AssertNotifiedOnceAsync(fixture, life.SupplierUserId, NotificationTypes.ProposalWithdrawn);
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.ProposalWithdrawn);
    }

    private sealed class FailingErpAdapter : MotsSupplierPortal.Application.Common.IErpPurchaseOrderAdapter
    {
        public Task<string> CreatePurchaseOrderAsync(Guid awardId, string rfqReferenceCode, CancellationToken ct) =>
            throw new InvalidOperationException("ERP is unavailable");
    }

    private async Task<AwardLifecycle> ExecutedAwardAsync(string label)
    {
        var life = await EvaluatedRfqAsync(label);
        var otherManager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager,
            await OrganizationIdOfAsync(life.RfqCode));

        await life.Officer.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/award/recommend", new
        {
            winningProposalId = life.WinningProposalId,
            justificationAr = "الأفضل", justificationEn = "Best",
        });
        await life.Officer.PostAsync($"/api/v1/rfqs/{life.RfqCode}/award/route-for-approval", null);
        await life.Manager.PostAsync($"/api/v1/rfqs/{life.RfqCode}/award/approve", null);

        var execute = await life.Manager.PostAsync($"/api/v1/rfqs/{life.RfqCode}/award/execute", null);
        execute.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await execute.Content.ReadAsStringAsync());

        _ = otherManager;
        return life;
    }

    private async Task<Guid> OrganizationIdOfAsync(string rfqCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Rfqs.Where(r => r.ReferenceCode == rfqCode).Select(r => r.OrganizationId).FirstAsync();
    }

    [Fact]
    public async Task A_successful_ERP_sync_notifies_procurement()
    {
        var life = await ExecutedAwardAsync("ErpOk");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MotsSupplierPortal.Infrastructure.Awards.AwardErpSyncJob>()
                .RunAsync(CancellationToken.None);
        }

        // §3.4 "ErpPoRequested -> ErpPoSynced | In-app to procurement".
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.AwardErpSynced);
    }

    [Fact]
    public async Task A_failed_ERP_sync_alerts_a_system_admin_and_the_award_still_stands()
    {
        var life = await ExecutedAwardAsync("ErpDown");

        // The seeded platform administrator, read from the database rather than created through the
        // staff test client - a system_admin is not organization-scoped and the client helper's
        // login path expects one.
        Guid adminId;
        await using (var adminScope = fixture.Services.CreateAsyncScope())
        {
            var adminDb = adminScope.ServiceProvider.GetRequiredService<AppDbContext>();
            adminId = await (from ur in adminDb.UserRoles
                             join r in adminDb.Roles on ur.RoleId equals r.Id
                             where r.Name == Roles.SystemAdmin
                             select ur.UserId).FirstAsync();
        }

        await using var fakeFactory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<MotsSupplierPortal.Application.Common.IErpPurchaseOrderAdapter, FailingErpAdapter>()));
        await using (var scope = fakeFactory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MotsSupplierPortal.Infrastructure.Awards.AwardErpSyncJob>()
                .RunAsync(CancellationToken.None);
        }

        // §3.4 "ErpPoRequested -> ErpPoFailed | Alert to `system_admin`", not organization-scoped.
        await AssertNotifiedOnceAsync(fixture, adminId, NotificationTypes.AwardErpFailed);

        // BRULE-099: the notification exists BECAUSE the award stands. A delivery concern must never
        // undo a committed award, and this is the assertion that says so.
        await using var verify = fixture.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var award = await db.Awards.FirstAsync(a => db.Rfqs.Any(r => r.Id == a.RfqId && r.ReferenceCode == life.RfqCode));
        award.State.Should().Be(MotsSupplierPortal.Domain.Awards.AwardState.Awarded);
    }
}
