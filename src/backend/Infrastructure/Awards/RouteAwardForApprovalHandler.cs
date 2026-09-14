// Routing a recommendation for approval, and moving the tender into its approval state.
//
//
// IT REFUSES EVERY STATE OUTSIDE THE LEGITIMATE THREE
//
// This used to move the tender only from one state and otherwise do nothing, which was correct for the
// legitimate re-route cycle after a rejection, where moving it again would wrongly throw.
//
// But it also silently did nothing for every OTHER state: cancelled, awarded, completed. The award could advance
// to awaiting approval on a dead or already-concluded tender, which is a real cross-aggregate gap.
//
// So the states are named and everything else is refused, rather than only the happy path being handled. The
// written process's own source state for this step is the recommendation state; the older one stays because
// every tender written before that state existed reaches here from it, and the approval state stays for the
// reject-and-re-route cycle.
//
// The new approval step is forced to the inserted state explicitly, for the reason the evaluation assignment
// handler's header explains.
//
//
// THE APPROVER POOL IS NOTIFIED, AND THAT IS A DOCUMENTED JUDGEMENT CALL
//
// Nothing in the identity domain resolves who "the approver" is. The approval permission is a claim held by a
// role, not a single identifiable person and not a queryable list of candidates.
//
// A segregation rule saying the approver must differ from the recommender does not by itself say WHICH holder of
// that claim should be paged.
//
// This is the same open design question the award work flagged when it was first built rather than a new gap, and
// it is reported rather than answered by inventing a routing rule.

namespace MotsSupplierPortal.Infrastructure.Awards;

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

public sealed class RouteAwardForApprovalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRouteAwardForApprovalHandler
{
    public async Task<AwardMutationResult> HandleAsync(RouteAwardForApprovalCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, award) = loaded.Value;

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
        foreach (var approval in award.Approvals.Where(a => !existingApprovalIds.Contains(a.Id)))
        {
            db.Entry(approval).State = EntityState.Added;
        }

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
