// A supplier submits their bid. The safety-critical endpoint.
//
// The closing time and the sets of required lines and mandatory requirements are read off the loaded tender
// and handed to the bid as plain facts. The domain method, not this handler, is what refuses a late
// submission, using the server's own clock.
//
// The bid's own method records the two ambiguities it cannot enforce without inventing a number: which
// documents gate a submission, and any minimum validity period the tender might require.
//
//
// TWO REFUSALS THAT MUST NOT BE CONFLATED
//
// Submission throws for two different reasons: the bid is in the wrong state, or the bid is incomplete. The
// written contract gives those different answers, a conflict for the first and an unprocessable-entity naming
// what is missing for the second.
//
// Mapping both to a conflict would tell a supplier with an unpriced line that their bid had moved on.
//
// So the source state is captured before the call and attached only when the refusal is genuinely about state,
// and the incompleteness is caught first and separately. A refusal about the submission window keeps its own
// answer, because that tells a supplier something different again.

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

public sealed class SubmitProposalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : ISubmitProposalHandler
{
    public async Task<ProposalResult> HandleAsync(SubmitProposalCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        if (rfq.SubmissionClosesAt is null) return new ProposalResult.InvalidState("This RFQ has no submission close date set.");
        var requiredItemIds = rfq.Items.Where(i => !i.IsOptional).Select(i => i.Id).ToHashSet();
        var mandatoryRequirementIds = rfq.Requirements.Where(r => r.IsMandatory).Select(r => r.Id).ToHashSet();

        var submittableState = proposal!.State == ProposalState.Draft;

        try
        {
            proposal.Submit(rfq.State == RfqState.SubmissionOpen, rfq.SubmissionClosesAt.Value, requiredItemIds, mandatoryRequirementIds);
        }
        catch (ProposalIncompleteException ex)
        {
            return new ProposalResult.Incomplete(ex.Error, ex.Message);
        }
        catch (DomainException ex)
        {
            return submittableState
                ? new ProposalResult.InvalidState(ex.Message)
                : new ProposalResult.InvalidState(ex.Message, proposal.State);
        }

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_submitted", scope.UserId, referenceCode: proposal.ReferenceCode,
            fromState: nameof(ProposalState.Draft), toState: nameof(ProposalState.Submitted), ct: ct);
        await db.SaveChangesAsync(ct);

        if (scope.UserId is not null)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendProposalSubmittedEmailAsync(scope.UserId.Value, proposal.Id, CancellationToken.None));
        }

        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}
