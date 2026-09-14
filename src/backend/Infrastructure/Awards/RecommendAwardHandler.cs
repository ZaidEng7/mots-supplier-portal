// An officer recommends a winner.
//
// Two facts must hold first, and both live outside the award: the evaluation must be finalised, and the
// recommended bid must have passed technical qualification in that finalised evaluation.
//
// They are resolved here before the domain method is called, which is the same split every other
// cross-aggregate guard in this codebase uses.
//
// The same endpoint handles the first recommendation and a re-recommendation after a rejection, because the
// written table names the same permission and the same actor for both.
//
//
// THE WINNER ARRIVES AS A PUBLIC CODE, RESOLVED WITHIN THIS TENDER
//
// So a code from another tender resolves to nothing here rather than to somebody else's bid.
//
// The internal identifier is still accepted, because removing a request field is a breaking change and this
// endpoint shipped taking one. The code wins when both arrive: it is the identifier the contract sanctions, and
// a caller sending two that disagree has a bug either way.
//
//
// AN UNRESOLVED TIE AT THE TOP BLOCKS A RECOMMENDATION
//
// Because the ordering between the tied bids came from nothing a rule decided.
//
// It is checked on the top rank rather than on the recommended bid. Recommending the loser of an unresolved tie
// is the same problem wearing different clothes, and both are refused until a person has put their name to the
// ordering.
//
//
// ELIGIBILITY CAN NO LONGER MEAN "STILL SUBMITTED"
//
// That test was written when the middle of the bid lifecycle was unreachable and every bid sat in submitted
// until it was awarded.
//
// The written award path now runs through shortlisted, so the eligible states are the three a live bid can be in
// before the offer. Submitted stays valid for a tender that never went through evaluation intake.
//
//
// RECOMMENDING IS ALSO WHAT MOVES THE TENDER
//
// The written table names THIS operation's permission for the tender's own move, so recording the recommendation
// is the trigger rather than a second endpoint.
//
// It is guarded on the shortlisting state, so a tender that reached the award path before that state existed is
// untouched and still routes directly.
//
// The approver POOL is notified, because nothing in the identity domain resolves a single named approver from
// the approval permission. Reported as the open business question it is.

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

public sealed class RecommendAwardHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRecommendAwardHandler
{
    public async Task<AwardMutationResult> HandleAsync(RecommendAwardCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, existingAward) = loaded.Value;

        var evaluation = await db.Evaluations.Include(e => e.Results)
            .FirstOrDefaultAsync(e => e.RfqId == rfq.Id, ct);
        if (evaluation is null || evaluation.State != EvaluationState.Finalized)
        {
            return new AwardMutationResult.InvalidState("Cannot recommend an award: the evaluation has not been finalized.");
        }
        var winner = command.WinningProposalCode is { Length: > 0 } code
            ? await db.Proposals.FirstOrDefaultAsync(p => p.ReferenceCode == code && p.RfqId == rfq.Id, ct)
            : command.WinningProposalId is { } id
                ? await db.Proposals.FirstOrDefaultAsync(p => p.Id == id && p.RfqId == rfq.Id, ct)
                : null;

        if (winner is null)
        {
            return new AwardMutationResult.InvalidState("The recommended proposal is not eligible for award.");
        }

        var result = evaluation.Results.FirstOrDefault(r => r.ProposalId == winner.Id);
        if (result is null || !result.TechnicallyQualified)
        {
            return new AwardMutationResult.InvalidState("Cannot recommend this proposal: it did not pass technical qualification in the finalized evaluation.");
        }

        if (evaluation.Results.Any(r => r.TieUnresolved && r.Rank == 1))
        {
            return new AwardMutationResult.InvalidState(
                "Cannot recommend an award: the top of the ranking is a tie that no tie-break rule resolved. Resolve it with a reason first.");
        }
        var proposal = winner;
        if (proposal is null || proposal.State is not (ProposalState.Submitted or ProposalState.UnderReview or ProposalState.Shortlisted))
        {
            return new AwardMutationResult.InvalidState("The recommended proposal is not eligible for award.");
        }

        Award award;
        string action;
        try
        {
            if (existingAward is null)
            {
                award = Award.Recommend(rfq.Id, winner.Id, command.JustificationAr, command.JustificationEn, scope.UserId!.Value);
                db.Awards.Add(award);
                action = "award.recommended";
            }
            else
            {
                award = existingAward;
                award.ReRecommend(winner.Id, command.JustificationAr, command.JustificationEn, scope.UserId!.Value);
                action = "award.re_recommended";
            }
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }

        if (rfq.State == RfqState.Shortlisting)
        {
            rfq.RecordRecommendation();

            NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqRecommendationRecorded,
                await NotificationRecipients.AwardApproversAsync(db, rfq.OrganizationId, ct),
                $"{NotificationTypes.RfqRecommendationRecorded}:{rfq.Id}:{award.RecommendationRevision}",
                new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

            await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_recommendation_recorded", scope.UserId,
                referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.Shortlisting),
                toState: nameof(RfqState.Recommendation), ct: ct);
        }

        NotificationOutbox.EnqueueMany(db,
            action == "award.re_recommended" ? NotificationTypes.AwardReRecommended : NotificationTypes.AwardRecommended,
            await NotificationRecipients.AwardApproversAsync(db, rfq.OrganizationId, ct),
            $"{action}:{award.Id}:{award.RecommendationRevision}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["awardId"] = award.Id.ToString() });

        await auditLogger.LogAsync("Award", award.Id, action, scope.UserId, referenceCode: rfq.ReferenceCode,
            toState: nameof(AwardState.Recommended), reason: null, ct: ct);
        await db.SaveChangesAsync(ct);
        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode, await AwardWinner.CodeAsync(db, award, ct)));
    }
}
