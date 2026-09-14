// Builds the guided workspace: a view gathering a tender, its bids, its evaluation and its award, storing
// nothing of its own.
//
// The list of stages is deliberately shorter than the full set of tender states. Three of those states
// cannot be reached by any method in this build, and a guided workspace that showed a stage nothing can
// ever enter would be actively misleading.
//
// The next actions are genuinely permission-aware, resolved on the server from the caller's own token and
// never from a client hint. The same action can be offered to a manager and withheld from an officer
// looking at the same tender, and the blocked reason explains whichever of the permission or the domain
// precondition is failing.

namespace MotsSupplierPortal.Infrastructure.Workspace;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Workspace;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

public sealed class GetWorkspaceHandler(AppDbContext db, IScopeContext scope) : IGetWorkspaceHandler
{
    private static readonly RfqState[] ReachableStages =
    [
        RfqState.Draft, RfqState.InternalReview, RfqState.Approved, RfqState.Published,
        RfqState.SubmissionOpen, RfqState.SubmissionClosed, RfqState.UnderEvaluation,
        RfqState.Clarification, RfqState.Shortlisting, RfqState.Recommendation,
        RfqState.AwardApproval, RfqState.Awarded, RfqState.Completed,
    ];

    public async Task<WorkspaceDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return null;
        var rfq = await db.Rfqs.AsSplitQuery().Include(r => r.Items).Include(r => r.Invitations)
            .FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return null;

        var submittedProposalCount = await db.Proposals.CountAsync(p => p.RfqId == rfq.Id && ProposalStates.InEvaluation.Contains(p.State), ct);
        var evaluation = await db.Evaluations.Include(e => e.Assignments)
            .FirstOrDefaultAsync(e => e.RfqId == rfq.Id, ct);
        var award = await db.Awards.FirstOrDefaultAsync(a => a.RfqId == rfq.Id, ct);
        var unresolvedClarifications = await db.Clarifications.CountAsync(c => c.RfqId == rfq.Id && c.Answer == null, ct);

        var isCancelled = rfq.State == RfqState.Cancelled;
        var stages = isCancelled
            ? []
            : ReachableStages.Select(s => new WorkspaceStageDto(s.ToString(), s == rfq.State, s < rfq.State)).ToList();

        var actions = isCancelled ? [] : BuildNextActions(rfq, submittedProposalCount, unresolvedClarifications, evaluation, award);

        return new WorkspaceDto(
            rfq.ReferenceCode, rfq.State.ToString(), isCancelled,
            submittedProposalCount, evaluation?.State.ToString(), award?.State.ToString(),
            stages, actions);
    }

    private List<WorkspaceActionDto> BuildNextActions(
        Rfq rfq, int submittedProposalCount, int unresolvedClarifications, EvaluationAggregate? evaluation, Award? award)
    {
        var actions = new List<WorkspaceActionDto>();

        switch (rfq.State)
        {
            case RfqState.Draft:
                actions.Add(BuildAction("submit_review", "إرسال للمراجعة الداخلية", "Submit for internal review", Permissions.RfqSubmitReview,
                    DraftBlockers(rfq)));
                break;

            case RfqState.InternalReview:
                actions.Add(BuildAction("approve_rfq", "الموافقة على الطلب", "Approve the RFQ", Permissions.RfqApprove, null));
                actions.Add(BuildAction("return_for_edits", "إعادة للتعديل", "Return for edits", Permissions.RfqReview, null));
                break;

            case RfqState.Approved:
                actions.Add(BuildAction("publish_rfq", "نشر الطلب", "Publish the RFQ", Permissions.RfqPublish, null));
                break;

            case RfqState.Published:
                actions.Add(SystemAction("awaiting_submission_window", "بانتظار فتح باب التقديم آلياً", "Awaiting the submission window to open automatically"));
                break;

            case RfqState.SubmissionOpen:
                actions.Add(BuildAction("close_submission", "إغلاق باب التقديم مبكراً", "Close submission early", Permissions.RfqClose, null));
                break;

            case RfqState.SubmissionClosed:
                actions.Add(BuildAction("open_evaluation", "فتح التقييم", "Open evaluation", Permissions.EvaluationOpen,
                    submittedProposalCount == 0 ? ("لا توجد عروض مقدَّمة.", "0 proposals submitted.") : null));
                break;

            case RfqState.UnderEvaluation:
                actions.AddRange(NextEvaluationActions(evaluation, unresolvedClarifications));
                break;

            case RfqState.AwardApproval:
                actions.AddRange(NextAwardActions(award));
                break;

            case RfqState.Awarded:
                actions.Add(SystemAction("awaiting_erp_sync", "بانتظار مزامنة أمر الشراء مع نظام تخطيط الموارد", "Awaiting ERP Purchase Order sync"));
                break;

            case RfqState.Completed:
                actions.Add(SystemAction("completed", "اكتملت دورة حياة الطلب", "The RFQ lifecycle is complete"));
                break;
        }

        return actions;
    }

    private static (string Ar, string En)? DraftBlockers(Rfq rfq)
    {
        var reasons = new List<(string Ar, string En)>();

        if (rfq.Items.Count == 0)
        {
            reasons.Add(("لا توجد بنود بعد.", "No items yet."));
        }
        if (rfq.SubmissionOpensAt is null || rfq.SubmissionClosesAt is null)
        {
            reasons.Add(("لم يتم تحديد تواريخ التقديم.", "Submission dates not set."));
        }
        else if (rfq.SubmissionOpensAt <= DateTimeOffset.UtcNow || rfq.SubmissionClosesAt <= DateTimeOffset.UtcNow)
        {
            reasons.Add(("تواريخ التقديم يجب أن تكون في المستقبل.", "The submission window has already started."));
        }
        if (rfq.EvaluationTemplateId is null)
        {
            reasons.Add(("لم يتم ربط قالب تقييم.", "No evaluation template bound."));
        }
        if (rfq.Invitations.Count == 0)
        {
            reasons.Add(("لم تتم دعوة أي مورد بعد.", "No supplier invited yet."));
        }

        return reasons.Count == 0
            ? null
            : (string.Join(" ", reasons.Select(r => r.Ar)), string.Join(" ", reasons.Select(r => r.En)));
    }

    private List<WorkspaceActionDto> NextEvaluationActions(EvaluationAggregate? evaluation, int unresolvedClarifications)
    {
        if (evaluation is null)
        {
            return [SystemAction("evaluation_not_started", "لم يتم فتح التقييم بعد", "Evaluation has not been opened yet")];
        }
        return evaluation.State switch
        {
            EvaluationState.NotStarted or EvaluationState.Assigned =>
                [BuildAction("assign_evaluators", "تعيين المقيّمين", "Assign evaluators", Permissions.EvaluationAssign, null)],
            EvaluationState.InProgress =>
                [SystemAction("evaluation_in_progress", "التقييم قيد التنفيذ من قبل المقيّمين", "Evaluators are scoring")],
            EvaluationState.EvaluatorSubmitted =>
                [BuildAction("consolidate_evaluation", "توحيد نتائج التقييم", "Consolidate evaluation results", Permissions.EvaluationConsolidate, null)],
            EvaluationState.Consolidated =>
                [BuildAction("finalize_evaluation", "اعتماد التقييم نهائياً", "Finalize evaluation", Permissions.EvaluationFinalize,
                    unresolvedClarifications > 0 ? ($"يوجد {unresolvedClarifications} استيضاح غير مُجاب.", $"{unresolvedClarifications} clarification(s) still unanswered.") : null)],
            EvaluationState.Finalized =>
                [BuildAction("recommend_award", "ترشيح الفائز", "Recommend the winning proposal", Permissions.AwardRecommend, null)],
            _ => [],
        };
    }

    private List<WorkspaceActionDto> NextAwardActions(Award? award)
    {
        if (award is null)
        {
            return [BuildAction("recommend_award", "ترشيح الفائز", "Recommend the winning proposal", Permissions.AwardRecommend, null)];
        }
        return award.State switch
        {
            AwardState.Recommended =>
                [BuildAction("route_for_approval", "إرسال للاعتماد", "Route for approval", Permissions.AwardRecommend, null)],
            AwardState.PendingApproval =>
                [
                    BuildAction("approve_award", "اعتماد الترسية", "Approve the award", Permissions.AwardApprove, null),
                    BuildAction("reject_award", "رفض الترشيح", "Reject the recommendation", Permissions.AwardReject, null),
                ],
            AwardState.Rejected =>
                [BuildAction("re_recommend_award", "إعادة الترشيح", "Re-recommend a winner", Permissions.AwardRecommend, null)],
            AwardState.Approved =>
                [BuildAction("execute_award", "إصدار الترسية", "Issue the award", Permissions.AwardApprove, null)],
            _ => [],
        };
    }

    private WorkspaceActionDto BuildAction(string action, string labelAr, string labelEn, string permission, (string Ar, string En)? blocker)
    {
        var hasPermission = scope.HasPermission(permission);
        var permitted = hasPermission && blocker is null;
        string? reasonAr = null, reasonEn = null;
        if (!permitted)
        {
            reasonAr = blocker?.Ar ?? "لا تملك الصلاحية اللازمة لهذا الإجراء.";
            reasonEn = blocker?.En ?? "You do not hold the permission required for this action.";
        }
        return new WorkspaceActionDto(action, labelAr, labelEn, permitted, reasonAr, reasonEn);
    }

    private static WorkspaceActionDto SystemAction(string action, string labelAr, string labelEn) =>
        new(action, labelAr, labelEn, Permitted: false, "هذه الخطوة تلقائية أو بانتظار طرف آخر.", "This step is automatic or awaiting another party.");
}
