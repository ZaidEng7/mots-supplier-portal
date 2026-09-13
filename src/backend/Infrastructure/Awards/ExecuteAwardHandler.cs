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

namespace MotsSupplierPortal.Infrastructure.Awards;

/// <summary>FEAT-14.4/14.5/14.6/14.7, FR-AWD-004/005/006/008: "execute award" - the whole
/// win/lose/RFQ/outbox side effect happens inside ONE SaveChangesAsync call, so there is never a
/// window where the winner is Awarded but a loser is still Submitted, or the RFQ hasn't moved yet.
/// The comparison snapshot (FEAT-14.7) is captured from the SAME IGetComparisonHandler EPIC-12
/// already built, frozen as JSON on the Award row at this exact moment - never re-queried live once
/// Awarded.</summary>
public sealed class ExecuteAwardHandler(
    AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IGetComparisonHandler comparisonHandler,
    IBackgroundJobClient backgroundJobs)
    : IExecuteAwardHandler
{
    public async Task<AwardMutationResult> HandleAsync(ExecuteAwardCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, award) = loaded.Value;

        var comparison = await comparisonHandler.HandleAsync(rfq.ReferenceCode, ct);
        var snapshotJson = JsonSerializer.Serialize(comparison);

        try
        {
            award.ExecuteAward(snapshotJson);
            rfq.MarkAwarded();
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }

        // The losers. Same widening as the winner's eligibility check above, and for the same
        // reason: after evaluation intake these sit in UnderReview or Shortlisted, and a filter on
        // Submitted alone would silently leave them in an evaluation state forever while the RFQ
        // completed around them.
        // T-064: AwardOffered joins the predicate, and it is not optional - approve now moves the
        // winner there, so without it the WINNER falls out of this query and is never awarded while
        // the RFQ completes around it. Third batch running in which a widened state machine had a
        // query filtering on the states either side of it.
        var proposals = await db.Proposals
            .Where(p => p.RfqId == rfq.Id
                && (p.State == ProposalState.Submitted
                    || p.State == ProposalState.UnderReview
                    || p.State == ProposalState.Shortlisted
                    || p.State == ProposalState.AwardOffered))
            .ToListAsync(ct);
        foreach (var proposal in proposals)
        {
            // EPIC-13/FEAT-13.3 audit finding: this loop mutates every Proposal's own State but
            // previously logged nothing under "Proposal" - only the Award and Rfq rows were
            // audited, even though BUSINESS-PROCESSES.md §4.1 names proposal.awarded/
            // proposal.not_selected as their own audited events.
            if (proposal.Id == award.WinningProposalId)
            {
                proposal.Award();
                await auditLogger.LogAsync("Proposal", proposal.Id, "proposal.awarded", scope.UserId,
                    referenceCode: proposal.ReferenceCode, toState: nameof(ProposalState.Awarded), ct: ct);
            }
            else
            {
                proposal.MarkNotSelected();
                await auditLogger.LogAsync("Proposal", proposal.Id, "proposal.not_selected", scope.UserId,
                    referenceCode: proposal.ReferenceCode, toState: nameof(ProposalState.NotSelected), ct: ct);
            }
        }

        db.OutboxMessages.Add(new Domain.Common.OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = "AwardApproved",
            PayloadJson = JsonSerializer.Serialize(new { AwardId = award.Id, RfqId = rfq.Id, RfqReferenceCode = rfq.ReferenceCode, award.WinningProposalId }),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await auditLogger.LogAsync("Award", award.Id, "award.awarded", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(AwardState.Approved), toState: nameof(AwardState.Awarded), ct: ct);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_awarded", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(RfqState.AwardApproval), toState: nameof(RfqState.Awarded), ct: ct);
        await db.SaveChangesAsync(ct);

        // Notify after the state change persists (InviteSupplierHandler's own established
        // pattern). Winner gets an award notice; every non-winning supplier gets a regret notice -
        // BRULE-082: no commercial detail of the winner is ever put in the loser's email, only the
        // fact of the outcome.
        var winnerSupplierId = proposals.First(p => p.Id == award.WinningProposalId).SupplierId;
        var winnerUserId = await db.Users.Where(u => u.SupplierId == winnerSupplierId)
            .Select(u => u.Id).FirstOrDefaultAsync(ct);
        if (winnerUserId != Guid.Empty)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendAwardIssuedEmailAsync(winnerUserId, rfq.Id, CancellationToken.None));
        }
        var loserSupplierIds = proposals.Where(p => p.Id != award.WinningProposalId).Select(p => p.SupplierId).ToList();
        var loserUserIds = await db.Users.Where(u => u.SupplierId != null && loserSupplierIds.Contains(u.SupplierId.Value)).Select(u => u.Id).ToListAsync(ct);
        foreach (var userId in loserUserIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendAwardRegretEmailAsync(userId, rfq.Id, CancellationToken.None));
        }

        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode, await AwardWinner.CodeAsync(db, award, ct)));
    }
}
