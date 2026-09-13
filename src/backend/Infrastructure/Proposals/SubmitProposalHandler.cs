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

/// <summary>FEAT-09.5/FR-PRP-006/007, the safety-critical endpoint: submissionCloseAt and the
/// required/mandatory id sets are resolved from the loaded Rfq here and handed to
/// Proposal.Submit as plain facts - the domain method (not this handler) is what actually refuses
/// a late submission, using the server's own clock. See Proposal.Submit's own doc comment for the
/// two flagged ambiguities (mandatory-document gating, RFQ minimum validity) this cannot enforce
/// without an invented number.</summary>
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

        // T-065: captured BEFORE the call, and attached below only when the refusal is genuinely
        // about state. Submit throws for two different reasons - a wrong source state, and an
        // incomplete proposal - and §12.5 gives those different answers: 409 for the first, 422
        // (PROPOSAL_ITEMS_REQUIRED) for the second. Mapping both to 409 would tell a supplier with
        // an unpriced item that their proposal had moved on.
        var submittableState = proposal!.State == ProposalState.Draft;

        try
        {
            proposal.Submit(rfq.State == RfqState.SubmissionOpen, rfq.SubmissionClosesAt.Value, requiredItemIds, mandatoryRequirementIds);
        }
        catch (ProposalIncompleteException ex)
        {
            // T-066: §12.5's 422 with a code naming what is missing. Caught FIRST and separately -
            // the window and wrong-state refusals below are still DomainException and still answer
            // 409/400, because those tell a supplier something different.
            return new ProposalResult.Incomplete(ex.Error, ex.Message);
        }
        catch (DomainException ex)
        {
            // Window refusals keep their 400; a wrong source state answers 409 with currentState.
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
