// EPIC-15 Phase 3: the notifications BUSINESS-PROCESSES.md's transition tables document and which fired nothing
// before this epic. The tests follow the tables in order - RFQ (§3.1), then evaluation and award (§3.3, §3.4),
// then proposal withdrawal (§3.2), then the ERP sync.
//
// Each assertion checks three things, because each can be wrong independently: that a notification was produced
// at all, that it reached the recipient the TABLE names rather than the one that was convenient to resolve, and
// that it was produced ONCE - a transition that notifies twice is as wrong as one that notifies nobody, and
// only a count catches it.
//
// RFQ, §3.1. Submitting for review notifies the procurement manager - "Draft -> InternalReview | In-app to
// procurement_manager" - and not the supplier, which is the negative that belongs beside it: an internal review
// step is not the supplier's business, and a notification centre is exactly where that would leak. Returning
// for edits and approving both notify the officer, per the same table.
//
// Opening and closing the submission window are clock-driven transitions but still state changes, so the
// notification travels the same Outbox in the same transaction (D-5). The window has to be in the future to be
// SET - deadlines are validated as future on creation - and in the past for the job to act on it, so it is
// moved in the database rather than waited out, which is what every other timeline test in this suite does. The
// job runs twice: one run opens the window, and the close query was evaluated against the state as it was
// before that, so production reaches the same place on its next scheduled tick. §3.1 names invitees for the
// open and invitees plus committee for the close, and the difference is deliberate: the committee is told about
// the CLOSE, not the open.
//
// Evaluation and award, §3.3 and §3.4. The evaluation setup uses a window that closes in three seconds when the
// test needs an evaluation, and one that stays open when the test is about something a supplier does while it is
// open - withdrawal is refused once the window closes, which is the rule and not an obstacle to route around.
// The scoring helper scores EVERY criterion of EVERY proposal, because submitting scores is refused until they
// are all in, which is the same rule the "all evaluators submitted" notification depends on, and every step in
// the helper asserts rather than firing and forgetting: a helper that silently fails produces tests that fail
// much later with "no notification", which says nothing about why.
//
// The evaluation transitions notify the groups the table names, in order: opened goes to the committee, all
// evaluators in goes to the officer, consolidated to the committee, finalized to the committee. The supplier is
// not on the committee, and an evaluation centre is exactly where that would leak - the control for that
// negative is the four assertions that DID land. Reopening notifies the assigned evaluators, per §3.3's
// "Consolidated -> InProgress | In-app to affected evaluators". Recusal notifies the officer, and §3.3 has no
// row for it: an invention, flagged in the catalogue and in the report.
//
// §3.4: recommended and routed go to the approver pool, and approved comes back to the officer. A supplier is
// never told about an award decision before it is executed - the regret and award notifications are §3.4's LAST
// transition, not this one. Rejecting notifies the officer and re-recommending notifies the approver again as a
// distinct type from the first recommendation, because the approver needs to know this one follows their own
// rejection.
//
// Withdrawing a proposal notifies both groups §3.2 names: "Draft / Submitted -> Withdrawn | In-app to supplier
// + procurement".
//
// The ERP sync. A success notifies procurement, per §3.4's "ErpPoRequested -> ErpPoSynced | In-app to
// procurement". A failure alerts a system_admin - §3.4's "ErpPoRequested -> ErpPoFailed | Alert to
// system_admin" - which is not organization-scoped, so the administrator is read from the database rather than
// created through the staff test client, whose login path expects an organization-scoped account. That
// assertion is scoped to THIS tender rather than counting every alert of this type the administrator holds: the
// sync job processes every award awaiting the integration and not only the one this test made, and the
// administrator is the seeded platform account every test shares, so a bare count would be a count of whatever
// else in the suite happened to fail first. The award still stands, which is BRULE-099: the notification exists
// BECAUSE the award stands, a delivery concern must never undo a committed award, and that is the assertion
// which says so.

namespace MotsSupplierPortal.Tests.Integration.Notifications;

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
using MotsSupplierPortal.Tests.Integration;

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

    [Fact]
    public async Task Submitting_an_RFQ_for_review_notifies_the_procurement_manager_and_not_the_supplier()
    {
        var life = await RfqInReviewAsync("SubmitReview");

        await AssertNotifiedOnceAsync(fixture, life.ManagerId, NotificationTypes.RfqSubmittedForReview);

        await AssertNotNotifiedAsync(fixture, life.SupplierUserId, NotificationTypes.RfqSubmittedForReview);
    }

    [Fact]
    public async Task Returning_an_RFQ_for_edits_notifies_the_officer()
    {
        var life = await RfqInReviewAsync("Returned");

        await life.Manager.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/return", new { comments = "Please add pricing detail" });

        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.RfqReturnedForEdits);
    }

    [Fact]
    public async Task Approving_an_RFQ_notifies_the_officer()
    {
        var life = await RfqInReviewAsync("Approved");

        await life.Manager.PostAsync($"/api/v1/rfqs/{life.RfqCode}/approve", null);

        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.RfqApproved);
    }

    [Fact]
    public async Task Opening_and_closing_the_submission_window_notifies_the_invitees()
    {
        var life = await RfqInReviewAsync("Window");

        await life.Manager.PostAsync($"/api/v1/rfqs/{life.RfqCode}/approve", null);
        await life.Officer.PostAsync($"/api/v1/rfqs/{life.RfqCode}/publish", null);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Rfqs.Where(r => r.ReferenceCode == life.RfqCode).ExecuteUpdateAsync(p => p
                .SetProperty(r => r.SubmissionOpensAt, DateTimeOffset.UtcNow.AddMinutes(-10))
                .SetProperty(r => r.SubmissionClosesAt, DateTimeOffset.UtcNow.AddMinutes(-5)));
        }

        for (var run = 0; run < 2; run++)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
        }

        await AssertNotifiedOnceAsync(fixture, life.SupplierUserId, NotificationTypes.RfqSubmissionOpened);
        await AssertNotifiedOnceAsync(fixture, life.SupplierUserId, NotificationTypes.RfqSubmissionClosed);
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.RfqSubmissionClosed);

        await AssertNotNotifiedAsync(fixture, life.OfficerId, NotificationTypes.RfqSubmissionOpened);
    }

    private sealed record AwardLifecycle(
        HttpClient Officer, Guid OfficerId, HttpClient Manager, Guid ManagerId,
        HttpClient Evaluator, Guid EvaluatorId, HttpClient SupplierA, string RfqCode,
        Guid WinningProposalId, string WinningProposalCode, Guid SupplierUserId);

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

        foreach (var criterionId in criterionIds)
        {
            var scored = await evaluator.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/my-evaluation/scores",
                new { proposalCode = await fixture.ProposalCodeAsync(proposalId), criterionId, rawScore = 80m, commentAr = (string?)null, commentEn = (string?)null });
            scored.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await scored.Content.ReadAsStringAsync());
        }

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

        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.EvaluationOpened);
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.EvaluatorSubmitted);
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.EvaluationConsolidated);
        await AssertNotifiedOnceAsync(fixture, life.ManagerId, NotificationTypes.EvaluationFinalized);

        await AssertNotNotifiedAsync(fixture, life.SupplierUserId, NotificationTypes.EvaluationConsolidated);
    }

    [Fact]
    public async Task Reopening_an_evaluation_notifies_the_assigned_evaluators()
    {
        var life = await EvaluatedRfqAsync("EvalReopen", finalize: false);

        await life.Manager.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/evaluation/reopen", new { reason = "Recount needed" });

        await AssertNotifiedOnceAsync(fixture, life.EvaluatorId, NotificationTypes.EvaluationReopened);
    }

    [Fact]
    public async Task Recusing_an_evaluator_notifies_the_officer()
    {
        var life = await EvaluatedRfqAsync("EvalRecuse", consolidate: false, finalize: false, submitScores: false);

        var recused = await life.Manager.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/evaluation/recuse",
            new { evaluatorUserId = life.EvaluatorId, reason = "Conflict of interest" });
        recused.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await recused.Content.ReadAsStringAsync());

        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.EvaluatorRecused);
    }

    [Fact]
    public async Task The_award_transitions_notify_the_approver_pool_then_the_officer()
    {
        var life = await EvaluatedRfqAsync("AwardFlow");

        await life.Officer.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/award/recommend", new
        {
            winningProposalCode = life.WinningProposalCode,
            justificationAr = "الأفضل سعراً", justificationEn = "Best value",
        });
        await life.Officer.PostAsync($"/api/v1/rfqs/{life.RfqCode}/award/route-for-approval", null);
        await life.Manager.PostAsync($"/api/v1/rfqs/{life.RfqCode}/award/approve", null);

        await AssertNotifiedOnceAsync(fixture, life.ManagerId, NotificationTypes.AwardRecommended);
        await AssertNotifiedOnceAsync(fixture, life.ManagerId, NotificationTypes.AwardRoutedForApproval);
        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.AwardApproved);

        await AssertNotNotifiedAsync(fixture, life.SupplierUserId, NotificationTypes.AwardApproved);
    }

    [Fact]
    public async Task Rejecting_an_award_notifies_the_officer_and_re_recommending_notifies_the_approver_again()
    {
        var life = await EvaluatedRfqAsync("AwardReject");

        await life.Officer.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/award/recommend", new
        {
            winningProposalCode = life.WinningProposalCode,
            justificationAr = "الأفضل", justificationEn = "Best",
        });
        await life.Officer.PostAsync($"/api/v1/rfqs/{life.RfqCode}/award/route-for-approval", null);
        await life.Manager.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/award/reject", new { reason = "Insufficient justification" });

        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.AwardRejected);

        await life.Officer.PostAsJsonAsync($"/api/v1/rfqs/{life.RfqCode}/award/recommend", new
        {
            winningProposalCode = life.WinningProposalCode,
            justificationAr = "مبرر أوفى", justificationEn = "Fuller justification",
        });

        await AssertNotifiedOnceAsync(fixture, life.ManagerId, NotificationTypes.AwardReRecommended);
    }

    [Fact]
    public async Task Withdrawing_a_proposal_notifies_the_supplier_and_the_committee()
    {
        var life = await EvaluatedRfqAsync("Withdraw", consolidate: false, finalize: false, closeWindow: false);

        await life.SupplierA.PostAsJsonAsync($"/api/v1/proposals/{life.WinningProposalCode}/withdraw",
            new { reason = "Cannot supply in time" });

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
            winningProposalCode = life.WinningProposalCode,
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

        await AssertNotifiedOnceAsync(fixture, life.OfficerId, NotificationTypes.AwardErpSynced);
    }

    [Fact]
    public async Task A_failed_ERP_sync_alerts_a_system_admin_and_the_award_still_stands()
    {
        var life = await ExecutedAwardAsync("ErpDown");

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

        var alerts = await NotificationTestHelper.ForRecipientAsync(fixture, adminId, NotificationTypes.AwardErpFailed);
        var mine = alerts.Where(n => n.DataJson is not null && n.DataJson.Contains(life.RfqCode, StringComparison.Ordinal)).ToList();

        mine.Should().ContainSingle($"'{NotificationTypes.AwardErpFailed}' must reach the administrator exactly once for this tender");
        mine[0].TitleAr.Should().NotBeNullOrWhiteSpace();

        await using var verify = fixture.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var award = await db.Awards.FirstAsync(a => db.Rfqs.Any(r => r.Id == a.RfqId && r.ReferenceCode == life.RfqCode));
        award.State.Should().Be(MotsSupplierPortal.Domain.Awards.AwardState.Awarded);
    }
}
