// Issuing the award: the winner, the losers, the tender and the integration request, in one commit.
//
// One save, so there is never a window in which the winner is awarded but a loser is still under review, or the
// tender has not moved yet.
//
// The comparison snapshot is captured from the same comparison the buyer's own screen builds, frozen as a
// document on the award at this exact moment, and never re-queried live once the award is issued.
//
//
// THE STATES IN THE LOSERS' QUERY ARE NOT OPTIONAL
//
// After evaluation intake, bids sit under review or shortlisted, and a filter on submitted alone would silently
// leave them in an evaluation state forever while the tender completed around them.
//
// The offered state joins the same list, and that one is load-bearing: approval now moves the winner there, so
// without it the WINNER falls out of this query and is never awarded while the tender completes around it.
//
// The third time in three passes that a widened state machine had a query filtering on the states either side of
// the new one.
//
//
// EVERY BID'S OWN TRANSITION IS AUDITED
//
// This loop changes every bid's state, and it used to log nothing under the bid: only the award and the tender
// were audited, even though the written process names the awarded and not-selected outcomes as their own events.
//
//
// THE EMAILS GO OUT AFTER THE STATE CHANGE PERSISTS
//
// Which is the established pattern here.
//
// The winner gets an award notice and every non-winning supplier gets a regret notice, and no commercial detail
// about the winner is ever put in a loser's email: only the fact of the outcome.

namespace MotsSupplierPortal.Infrastructure.Awards;

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

        var proposals = await db.Proposals
            .Where(p => p.RfqId == rfq.Id
                && (p.State == ProposalState.Submitted
                    || p.State == ProposalState.UnderReview
                    || p.State == ProposalState.Shortlisted
                    || p.State == ProposalState.AwardOffered))
            .ToListAsync(ct);
        foreach (var proposal in proposals)
        {
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
