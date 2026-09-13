using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>FEAT-07.8/BUSINESS-PROCESSES.md §3.1: cancel from any pre-Awarded state, reason
/// mandatory. FEAT-13.3 audit gap fix: notifies every invited supplier AND, if an Evaluation had
/// already been opened, every assigned evaluator - both had work in flight on this RFQ that just
/// became moot, and neither was told before this fix.</summary>
public sealed class CancelRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : ICancelRfqHandler
{
    public async Task<RfqMutationResult> HandleAsync(CancelRfqCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        var fromState = rfq.State;
        try
        {
            rfq.Cancel(command.Reason);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex, RfqState.Cancelled);
        }

        // A-9/BRULE-056, enforced for the first time. The rule says cancellation "voids open
        // invitations/proposals"; before this it notified everyone and moved nothing, so a Submitted
        // proposal stayed Submitted forever on a cancelled tender - and BRULE-056 carries no assumption
        // tag, which made that a confirmed rule going unenforced.
        //
        // Terminal proposals are left alone: a withdrawn bid was withdrawn, and an awarded one belongs
        // to an RFQ that could not have been cancelled.
        var liveProposals = await db.Proposals.Where(p => p.RfqId == rfq.Id).ToListAsync(ct);
        foreach (var proposal in liveProposals.Where(p => Proposal.AllowedNextFrom(p.State).Count > 0))
        {
            proposal.CancelWithRfq();

            NotificationOutbox.EnqueueMany(db, NotificationTypes.ProposalCancelled,
                await NotificationRecipients.SupplierUsersAsync(db, proposal.SupplierId, ct),
                $"{NotificationTypes.ProposalCancelled}:{proposal.Id}",
                new Dictionary<string, string?>
                {
                    ["rfqCode"] = rfq.ReferenceCode,
                    ["proposalCode"] = proposal.ReferenceCode,
                    ["proposalId"] = proposal.Id.ToString(),
                });

            await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_cancelled", scope.UserId,
                referenceCode: proposal.ReferenceCode, toState: nameof(ProposalState.Cancelled),
                reason: command.Reason, ct: ct);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_cancelled", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: fromState.ToString(), toState: nameof(RfqState.Cancelled), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);

        var invitedSupplierIds = rfq.Invitations.Select(i => i.SupplierId).ToList();
        if (invitedSupplierIds.Count > 0)
        {
            var supplierUserIds = await db.Users.Where(u => u.SupplierId != null && invitedSupplierIds.Contains(u.SupplierId.Value))
                .Select(u => u.Id).ToListAsync(ct);
            foreach (var userId in supplierUserIds)
            {
                backgroundJobs.Enqueue<EmailJobs>(job => job.SendRfqCancelledEmailAsync(userId, rfq.Id, CancellationToken.None));
            }
        }

        var evaluationId = await db.Evaluations.Where(e => e.RfqId == rfq.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        var evaluatorUserIds = evaluationId is null
            ? []
            : await db.EvaluationAssignments
                .Where(a => a.EvaluationId == evaluationId.Value && a.RecusedAt == null)
                .Select(a => a.EvaluatorUserId).Distinct().ToListAsync(ct);
        foreach (var userId in evaluatorUserIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendRfqCancelledEmailAsync(userId, rfq.Id, CancellationToken.None));
        }

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}
