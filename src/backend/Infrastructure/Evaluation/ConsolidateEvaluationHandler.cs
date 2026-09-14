// Consolidating the evaluators' scores into one ranked result, and moving the tender on.
//
//
// THE TWO TIE-BREAK FACTS THE SCORES CANNOT SUPPLY
//
// The commercial total and the moment of submission, loaded here because the evaluation has no access to
// bids.
//
// The totals are summed in memory rather than in the database, because a sum over a computed line total does
// not translate into a query. That lesson came from a dashboard tile.
//
// Reading the commercial total AT CONSOLIDATION is not a breach of the two-envelope seal. The seal is between
// the technical and commercial envelopes DURING scoring, and consolidation is exactly where the financial
// dimension is deliberately brought in.
//
//
// CONSOLIDATING IS ALSO WHAT MOVES THE TENDER AND THE BIDS
//
// The written process's transition out of evaluation names this operation's permission and names this
// operation's outcome as its guard. So consolidating is the trigger, rather than a second endpoint an officer
// would have to remember to call to keep the tender's state honest.
//
// The same holds one level down for the bids. Passing the thresholds is what shortlists a bid, and the
// technical-qualification flag IS that threshold comparison: it is what consolidation computes from the
// criteria's own thresholds, so this reads the result rather than re-deriving the rule.
//
// Bids that do NOT pass are left where they are rather than marked unsuccessful. The written process puts
// that outcome under the award recommendation, which is a later decision by a person, and marking them here
// would pre-empt it.
//
// New result rows are forced to the inserted state explicitly rather than left to the change tracker's
// heuristic, for the reason the assignment handler's header explains.

namespace MotsSupplierPortal.Infrastructure.Evaluation;

using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Globalization;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

public sealed class ConsolidateEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IConsolidateEvaluationHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(ConsolidateEvaluationCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        var existingResultIds = evaluation.Results.Select(r => r.Id).ToHashSet();

        var bids = await db.Proposals
            .Where(p => p.RfqId == rfq.Id)
            .Select(p => new { p.Id, p.SubmittedAt, Items = p.Items.Select(i => new { i.Quantity, i.UnitPrice, i.Discount }).ToList() })
            .ToListAsync(ct);
        var bidFacts = bids.ToDictionary(
            b => b.Id,
            b => new Domain.Evaluation.Evaluation.BidTieBreakFacts(
                b.Items.Count == 0 ? null : b.Items.Sum(i => (i.Quantity * i.UnitPrice) - (i.Discount ?? 0m)),
                b.SubmittedAt));

        try
        {
            evaluation.Consolidate(bidFacts);
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }
        foreach (var result in evaluation.Results.Where(r => !existingResultIds.Contains(r.Id)))
        {
            db.Entry(result).State = EntityState.Added;
        }

        if (rfq.State == RfqState.UnderEvaluation)
        {
            rfq.BeginShortlisting();

            NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqShortlistingStarted,
                await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
                $"{NotificationTypes.RfqShortlistingStarted}:{rfq.Id}",
                new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

            await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_shortlisting_started", scope.UserId,
                referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.UnderEvaluation),
                toState: nameof(RfqState.Shortlisting), ct: ct);
        }

        var qualified = evaluation.Results.Where(r => r.TechnicallyQualified).Select(r => r.ProposalId).ToHashSet();
        if (qualified.Count > 0)
        {
            var toShortlist = await db.Proposals
                .Where(p => p.RfqId == rfq.Id && p.State == ProposalState.UnderReview)
                .ToListAsync(ct);

            foreach (var proposal in toShortlist.Where(p => qualified.Contains(p.Id)))
            {
                proposal.Shortlist();
                await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_shortlisted", scope.UserId,
                    referenceCode: proposal.ReferenceCode,
                    fromState: nameof(ProposalState.UnderReview), toState: nameof(ProposalState.Shortlisted), ct: ct);
            }
        }

        NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluationConsolidated,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.EvaluationConsolidated}:{evaluation.Id}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["evaluationId"] = evaluation.Id.ToString() });

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_consolidated", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.Consolidated), ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq, await EvaluationProposalCodes.ForRfqAsync(db, rfq.Id, ct)));
    }
}
