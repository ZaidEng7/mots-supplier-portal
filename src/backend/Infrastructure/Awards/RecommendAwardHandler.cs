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

/// <summary>FEAT-14.1/FR-AWD-001, BRULE-071: "recorded only after evaluation is Finalized and the
/// recommended proposal passes all thresholds" - both cross-aggregate facts (Evaluation lives in
/// its own aggregate), resolved here before calling the domain method, same split as every other
/// cross-aggregate guard in this codebase. Handles both the first recommendation (no Award row yet)
/// and a re-recommendation after Rejected (Award.ReRecommend) through the same endpoint - the table
/// names both `award.recommend`, same actor.</summary>
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
        // T-068: the winner arrives as a public code, resolved to a bid ON THIS TENDER before anything
        // else looks at it - a code from another tender resolves to nothing here rather than to
        // somebody else's proposal.
        //
        // The GUID is still accepted, because removing a request field is a breaking change and this
        // endpoint shipped taking one (D-69). The code wins when both arrive: it is the identifier
        // §3 sanctions, and a caller sending two that disagree has a bug either way.
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

        // A-1/BRULE-069: an unresolved tie at the TOP of the ranking blocks a recommendation, because
        // the ordering between the tied bids came from nothing a rule decided. Checked on rank 1
        // rather than on the recommended proposal: recommending the loser of an unresolved tie is the
        // same problem wearing different clothes, and both are refused until a person has put their
        // name to the ordering.
        if (evaluation.Results.Any(r => r.TieUnresolved && r.Rank == 1))
        {
            return new AwardMutationResult.InvalidState(
                "Cannot recommend an award: the top of the ranking is a tie that no tie-break rule resolved. Resolve it with a reason first.");
        }
        var proposal = winner;
        // T-051: proposals now reach UnderReview and Shortlisted, so eligibility can no longer mean
        // "still Submitted" - that predicate was written when the middle of the lifecycle was
        // unreachable and every proposal sat in Submitted until it was awarded. §4.1's award path is
        // Shortlisted -> AwardOffered -> Awarded; Submitted stays valid for an RFQ that never went
        // through evaluation intake.
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

        // T3-36. §3.1: "Shortlisting | Recommendation | Record recommendation |
        // `procurement_officer`,`procurement_manager` / `award.recommend`". Same reasoning as
        // shortlisting: the table names THIS operation's permission for the RFQ's own move, so
        // recording the recommendation is the trigger. Guarded on Shortlisting, so an RFQ that
        // reached UnderEvaluation before T3-36 is untouched and still routes directly.
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

        // §3.4 "- -> Recommended | In-app to approver" and "Rejected -> Recommended | In-app to
        // approver". The APPROVER POOL: nothing in the Identity domain resolves a single named
        // approver from the AwardApprove claim, so this notifies everyone who could approve it.
        // Reported as the open business question it is.
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
