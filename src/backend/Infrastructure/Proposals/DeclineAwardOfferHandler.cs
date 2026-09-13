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

namespace MotsSupplierPortal.Infrastructure.Proposals;

/// <summary>
/// T-064, §4.1: <c>AwardOffered -&gt; Declined</c>, "Supplier declines ... Free the award for
/// alternate; RFQ returns to <c>Recommendation</c>".
///
/// <para>Supplier-side, so the proposal is loaded through the supplier's own scope - a decline on
/// someone else's award offer is the same 404 as a code that does not exist.</para>
///
/// <para><b>The RFQ moves in the same SaveChanges as the proposal.</b> A declined offer that left the
/// RFQ in AwardApproval would be an award nobody could act on: the offer is dead and the officer has
/// no route back to choosing an alternate. Two aggregates in one unit of work, for the same reason
/// AwardHandlers already does it - a window in which one has moved and the other has not is worse
/// than the coupling.</para>
/// </summary>
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

            // Only when the RFQ is actually awaiting the award decision. An RFQ that reached Awarded
            // by the direct path has no offer outstanding, so there is nothing to return.
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

        // §4.1: "In-app to procurement". The supplier's own users are NOT notified - they are the
        // ones who just declined, and BRULE-091's spirit is that a notification tells someone
        // something they do not already know.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalDeclined,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.ProposalDeclined}:{proposal.Id}",
            new Dictionary<string, string?>
            {
                ["rfqCode"] = rfq.ReferenceCode,
                ["proposalCode"] = proposal.ReferenceCode,
            });

        // The reason is audited and deliberately NOT in the notification payload - BRULE-091 keeps a
        // supplier's free text out of it, and the officer reads it on the screen the link opens.
        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal.declined", scope.UserId,
            referenceCode: proposal.ReferenceCode,
            fromState: nameof(ProposalState.AwardOffered), toState: nameof(ProposalState.Declined),
            reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}
