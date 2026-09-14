// A supplier declines an award they were offered.
//
// Supplier-side, so the bid is loaded through the supplier's own scope: declining somebody else's award offer
// is the same not-found as a code that does not exist.
//
//
// THE TENDER MOVES IN THE SAME COMMIT AS THE BID
//
// A declined offer that left the tender awaiting its award decision would be an award nobody could act on: the
// offer is dead and the officer has no route back to choosing an alternate.
//
// Two aggregates in one unit of work, for the same reason the award handlers already do it. A window in which
// one has moved and the other has not is worse than the coupling.
//
// It only returns the tender when the tender is actually awaiting that decision. One that reached its award by
// the direct path has no offer outstanding, so there is nothing to return.
//
//
// WHO IS TOLD, AND WHAT IS WITHHELD
//
// The procuring side. The supplier's own users are not notified: they are the ones who just declined, and a
// notification is meant to tell somebody something they do not already know.
//
// The reason is audited and deliberately kept out of the notification payload, because a supplier's free text
// does not belong there. The officer reads it on the screen the link opens.

namespace MotsSupplierPortal.Infrastructure.Proposals;

using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Infrastructure.Rfqs;

public sealed class DeclineAwardOfferHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IDeclineAwardOfferHandler
{
    public async Task<ProposalResult> HandleAsync(DeclineAwardOfferCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        var fromState = proposal.State;
        try
        {
            proposal.DeclineAward(command.Reason);

            if (rfq.State == RfqState.AwardApproval)
            {
                rfq.ReturnToRecommendation();
                await auditLogger.LogAsync("Rfq", rfq.Id, "rfq.returned_to_recommendation", scope.UserId,
                    referenceCode: rfq.ReferenceCode,
                    fromState: nameof(RfqState.AwardApproval), toState: nameof(RfqState.Recommendation), ct: ct);
            }
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, fromState);
        }

        NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalDeclined,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.ProposalDeclined}:{proposal.Id}",
            new Dictionary<string, string?>
            {
                ["rfqCode"] = rfq.ReferenceCode,
                ["proposalCode"] = proposal.ReferenceCode,
            });

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal.declined", scope.UserId,
            referenceCode: proposal.ReferenceCode,
            fromState: nameof(ProposalState.AwardOffered), toState: nameof(ProposalState.Declined),
            reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}
