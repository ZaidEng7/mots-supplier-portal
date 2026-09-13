// A manager approves a recommended award.
//
//
// SEGREGATION OF DUTIES IS ENFORCED HERE FIRST
//
// The approver may not be the recommender. This is the primary enforcement point, the one the written actor
// column names, and it runs before the domain's own repeat of the same check.
//
// Its own typed refusal is what lets the endpoint answer with a specific error code rather than a generic domain
// message.
//
// The winning supplier's active status is also checked here, at approval time, which is the written rule's own
// wording: at the moment of approval.
//
//
// APPROVAL IS WHERE THE OFFER BECOMES TRUE
//
// The written process makes the shortlisted bid an offer at this point, and the bid's own method explains why it
// is not made at recommendation time.
//
// Only from shortlisted. A tender that never shortlisted awards directly at execution, which is the path that
// already existed; forcing every award through the offer would break those, and the written process does not
// require it.
//
// The bid is non-null by the time that runs, because the supplier check above returns early when it is not.

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

public sealed class ApproveAwardHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IApproveAwardHandler
{
    public async Task<AwardMutationResult> HandleAsync(ApproveAwardCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, award) = loaded.Value;

        if (scope.UserId == award.RecommendedByUserId)
        {
            return new AwardMutationResult.SegregationOfDutiesViolation();
        }
        var proposal = await db.Proposals.FirstOrDefaultAsync(p => p.Id == award.WinningProposalId, ct);
        var supplier = proposal is null ? null : await db.Suppliers.FirstOrDefaultAsync(s => s.Id == proposal.SupplierId, ct);
        if (supplier is null || supplier.LifecycleState != SupplierLifecycleState.Active)
        {
            return new AwardMutationResult.SupplierNotActive();
        }

        try
        {
            award.Approve(scope.UserId!.Value);

            if (proposal!.State == ProposalState.Shortlisted)
            {
                proposal.OfferAward();
                await auditLogger.LogAsync("Proposal", proposal.Id, "proposal.award_offered", scope.UserId,
                    referenceCode: proposal.ReferenceCode,
                    fromState: nameof(ProposalState.Shortlisted), toState: nameof(ProposalState.AwardOffered), ct: ct);

                NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalAwardOffered,
                    await NotificationRecipients.SupplierUsersAsync(db, proposal.SupplierId, ct),
                    $"{NotificationTypes.ProposalAwardOffered}:{proposal.Id}",
                    new Dictionary<string, string?>
                    {
                        ["rfqCode"] = rfq.ReferenceCode,
                        ["proposalCode"] = proposal.ReferenceCode,
                    });
            }
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }

        NotificationOutbox.EnqueueMany(db, NotificationTypes.AwardApproved,
            await NotificationRecipients.RfqOwnerAsync(db, rfq, ct),
            $"{NotificationTypes.AwardApproved}:{award.Id}:{award.RecommendationRevision}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["awardId"] = award.Id.ToString() });

        await auditLogger.LogAsync("Award", award.Id, "award.approved", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(AwardState.PendingApproval), toState: nameof(AwardState.Approved), ct: ct);
        await db.SaveChangesAsync(ct);
        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode, await AwardWinner.CodeAsync(db, award, ct)));
    }
}
