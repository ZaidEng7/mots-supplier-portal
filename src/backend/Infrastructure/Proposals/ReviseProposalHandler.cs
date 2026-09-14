// A supplier revises their bid after the buyer asked for clarification.
//
// Supplier-side, so it loads through the supplier's own scope like every other bid action they take.
//
// The revision number is part of the de-duplication key, so a second revision is a second piece of news to the
// committee.

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

public sealed class ReviseProposalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IReviseProposalHandler
{
    public async Task<ProposalResult> HandleAsync(ReviseProposalCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        var fromState = proposal!.State;
        try
        {
            proposal.RecordRevision();
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, fromState);
        }

        NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalRevised,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.ProposalRevised}:{proposal.Id}:{proposal.RevisionNumber}",
            new Dictionary<string, string?>
            {
                ["rfqCode"] = rfq.ReferenceCode,
                ["proposalCode"] = proposal.ReferenceCode,
                ["proposalId"] = proposal.Id.ToString(),
            });

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_revised", scope.UserId,
            referenceCode: proposal.ReferenceCode,
            fromState: fromState.ToString(), toState: nameof(ProposalState.Revised), ct: ct);
        await db.SaveChangesAsync(ct);

        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}
