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

public sealed class WithdrawProposalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IWithdrawProposalHandler
{
    public async Task<ProposalResult> HandleAsync(WithdrawProposalCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        var fromState = proposal!.State;
        try
        {
            proposal.Withdraw(command.Reason, rfq.State == RfqState.SubmissionOpen);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, fromState);
        }

        // §3.2 "Draft / Submitted -> Withdrawn | In-app to supplier + procurement". Two groups: the
        // supplier's own users (so a colleague sees the withdrawal) and the RFQ's committee.
        var withdrawRecipients = await NotificationRecipients.SupplierUsersAsync(db, proposal.SupplierId, ct);
        withdrawRecipients.AddRange(await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct));
        NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalWithdrawn, withdrawRecipients,
            $"{NotificationTypes.ProposalWithdrawn}:{proposal.Id}",
            new Dictionary<string, string?>
            {
                ["rfqCode"] = rfq.ReferenceCode,
                ["proposalCode"] = proposal.ReferenceCode,
                ["proposalId"] = proposal.Id.ToString(),
            });

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_withdrawn", scope.UserId, referenceCode: proposal.ReferenceCode,
            fromState: fromState.ToString(), toState: nameof(ProposalState.Withdrawn), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}
