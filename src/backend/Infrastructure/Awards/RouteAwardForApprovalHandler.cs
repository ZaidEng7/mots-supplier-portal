using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Awards;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Awards;

/// <summary>FEAT-14.2/FR-AWD-002: routes the recommendation for approval and, in the same
/// handler/SaveChanges, moves the RFQ itself into AwardApproval (Rfq.EnterAwardApproval's own doc
/// comment covers why that guards on UnderEvaluation rather than a Recommendation state nothing can
/// produce yet).
///
/// <para><b>EPIC-13/FEAT-13.3 audit finding, left as a documented judgment call rather than a code
/// fix:</b> unlike RfqPublishHandler/CancelRfqHandler/AssignEvaluatorsHandler (all fixed this epic
/// to notify their real recipients), no email is enqueued here to "the approver" - because no
/// mechanism anywhere in the Identity domain resolves who that is. Permissions.AwardApprove is a
/// CLAIM held by a role (ProcurementManager), not a single identifiable user or a queryable list of
/// candidate approvers; a single-approver segregation-of-duties model (BRULE-077: approver must
/// differ from recommender) does not by itself say WHICH holder of that claim should be paged. This
/// is the same open design question EPIC-14 already flagged when Award was first built, not a new
/// gap introduced here - notifying "everyone with AwardApprove" would be a guess this codebase's own
/// audit trail conventions do not support without a real approver-assignment concept
/// (EPIC-15/notifications scope, unbuilt).</para></summary>
public sealed class RouteAwardForApprovalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRouteAwardForApprovalHandler
{
    public async Task<AwardMutationResult> HandleAsync(RouteAwardForApprovalCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, award) = loaded.Value;

        // EPIC-13/FEAT-13.2 stage-gate audit: this used to be `if (rfq.State == UnderEvaluation)
        // rfq.EnterAwardApproval();` - correct for the legitimate re-route cycle (RFQ already
        // AwardApproval from a prior Reject -> ReRecommend -> RouteForApproval pass, where a
        // second EnterAwardApproval() call would wrongly throw), but it silently no-op'd for EVERY
        // other RFQ state too - Cancelled, Awarded, Completed - letting Award.RouteForApproval()
        // succeed unconditionally regardless of RFQ state, a real cross-aggregate gap: the Award
        // could advance to PendingApproval on a dead or already-concluded RFQ. Explicitly refuse
        // every state outside the two legitimate ones instead of only handling the happy path.
        // T3-36 added Recommendation, which is §3.1's OWN source state for this row:
        // "Recommendation | AwardApproval | Route for approval | `procurement_officer` /
        // `award.recommend`". UnderEvaluation stays because every RFQ written before T3-36 reaches
        // here from it, and AwardApproval stays for the legitimate reject-and-re-route cycle.
        if (rfq.State is not (RfqState.Recommendation or RfqState.UnderEvaluation or RfqState.AwardApproval))
        {
            return new AwardMutationResult.InvalidState($"Cannot route award for approval: the RFQ is in state '{rfq.State}'.");
        }

        var existingApprovalIds = award.Approvals.Select(a => a.Id).ToHashSet();
        try
        {
            award.RouteForApproval();
            if (rfq.State is RfqState.Recommendation or RfqState.UnderEvaluation) rfq.EnterAwardApproval();
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }
        // EF's change-tracker misclassifies a brand-new child appended to an already-Included
        // collection as Modified (not Added) when the SAME SaveChanges also updates the owning
        // Award row's own State column - see EPIC-11's AssignEvaluatorsHandler for the first time
        // this was found; forcing the state explicitly for the row this call actually created
        // sidesteps that misdetection rather than relying on DetectChanges' fixup heuristic.
        foreach (var approval in award.Approvals.Where(a => !existingApprovalIds.Contains(a.Id)))
        {
            db.Entry(approval).State = EntityState.Added;
        }

        // §3.4 "Recommended -> PendingApproval | Email + in-app to approver(s)".
        NotificationOutbox.EnqueueMany(db, NotificationTypes.AwardRoutedForApproval,
            await NotificationRecipients.AwardApproversAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.AwardRoutedForApproval}:{award.Id}:{award.RecommendationRevision}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["awardId"] = award.Id.ToString() });

        await auditLogger.LogAsync("Award", award.Id, "award.pending_approval", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(AwardState.Recommended), toState: nameof(AwardState.PendingApproval), ct: ct);
        await db.SaveChangesAsync(ct);
        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode, await AwardWinner.CodeAsync(db, award, ct)));
    }
}
