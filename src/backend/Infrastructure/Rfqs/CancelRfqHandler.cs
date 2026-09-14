// Cancelling a tender, from any state before it has been awarded, with a mandatory reason.
//
//
// THE RULE SAYS CANCELLATION VOIDS THE OPEN BIDS, AND IT NOW DOES
//
// Before this it notified everyone and moved nothing, so a submitted bid stayed submitted forever on a
// cancelled tender. That rule carries no assumption tag, which made it a confirmed rule going unenforced.
//
// Terminal bids are left alone. A withdrawn bid was withdrawn, and an awarded one belongs to a tender that
// could not have been cancelled.
//
//
// WHO IS TOLD
//
// Every invited supplier, and, if an evaluation had already been opened, every assigned evaluator. Both had
// work in flight on this tender that just became moot, and neither was told before this.
//
// Each supplier is also told about their own bid separately, rather than only about the tender, because a bid
// moving to cancelled is a fact about their work.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

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
