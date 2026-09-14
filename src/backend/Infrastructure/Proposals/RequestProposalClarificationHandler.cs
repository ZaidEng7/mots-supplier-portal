// A buyer asks one bidder to clarify their bid.
//
// Buyer-side, so the bid is reached through the tender's own scope rather than the supplier's, because the
// actor here is procurement.
//
// The scope test is in the query: the bid must belong to a tender in the caller's organization, and a miss is
// indistinguishable from a code that never existed.
//
// The revision number is part of the de-duplication key, so a second round of clarification on a revised bid is
// a second piece of news.

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

public sealed class RequestProposalClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IRequestProposalClarificationHandler
{
    public async Task<ProposalResult> HandleAsync(RequestProposalClarificationCommand command, CancellationToken ct)
    {
        var proposal = await db.Proposals
            .FirstOrDefaultAsync(p => p.ReferenceCode == command.ProposalReferenceCode, ct);
        if (proposal is null) return new ProposalResult.NotFoundOrNotInvited();

        var rfq = await db.Rfqs.FirstOrDefaultAsync(
            r => r.Id == proposal.RfqId && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return new ProposalResult.NotFoundOrNotInvited();

        var fromState = proposal.State;
        try
        {
            proposal.RequestClarification(command.Reason);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, fromState);
        }

        NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalClarificationRequested,
            await NotificationRecipients.SupplierUsersAsync(db, proposal.SupplierId, ct),
            $"{NotificationTypes.ProposalClarificationRequested}:{proposal.Id}:{proposal.RevisionNumber}",
            new Dictionary<string, string?>
            {
                ["rfqCode"] = rfq.ReferenceCode,
                ["proposalCode"] = proposal.ReferenceCode,
                ["proposalId"] = proposal.Id.ToString(),
            });

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_clarification_requested", scope.UserId,
            referenceCode: proposal.ReferenceCode, reason: command.Reason,
            fromState: fromState.ToString(), toState: nameof(ProposalState.ClarificationRequested), ct: ct);
        await db.SaveChangesAsync(ct);

        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}
