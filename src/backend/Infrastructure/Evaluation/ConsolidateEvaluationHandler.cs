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

namespace MotsSupplierPortal.Infrastructure.Evaluation;

/// <summary>FEAT-11.6/FR-EVL-007, procurement_officer,procurement_manager / evaluation.consolidate.</summary>
public sealed class ConsolidateEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IConsolidateEvaluationHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(ConsolidateEvaluationCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        var existingResultIds = evaluation.Results.Select(r => r.Id).ToHashSet();

        // A-1/BRULE-069: the two tie-break rungs the scores cannot supply. Loaded here because the
        // aggregate has no access to proposals, and materialised before summing because a SQL SUM over
        // a computed property does not translate (the lesson from EPIC-18's awarded-value tile).
        //
        // Reading the commercial total at CONSOLIDATION is not a two-envelope breach: OQ-009's seal is
        // between the technical and commercial envelopes DURING scoring, and consolidation is where
        // the financial dimension is deliberately brought in.
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
        // See AssignEvaluatorsHandler's own comment on why this is forced explicitly rather than
        // left to DetectChanges' fixup heuristic.
        foreach (var result in evaluation.Results.Where(r => !existingResultIds.Contains(r.Id)))
        {
            db.Entry(result).State = EntityState.Added;
        }

        // T3-36. §3.1: "UnderEvaluation | Shortlisting | Begin shortlisting |
        // `procurement_officer`,`procurement_manager` / `evaluation.consolidate` | Evaluation
        // Consolidated/Finalized". The permission the table names for that transition is THIS
        // operation's permission, and the guard it names is this operation's outcome - so
        // consolidating is the trigger, rather than a second endpoint an officer would have to
        // remember to call to keep the RFQ's state honest.
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

        // T-051, §4.1: "UnderReview -> Shortlisted | Passes thresholds |
        // procurement_officer,procurement_manager / evaluation.consolidate | Consolidated score >=
        // thresholds (§5)". Same trigger and same permission as the RFQ-level transition above, so
        // shortlisting a proposal is part of consolidating rather than a second action.
        //
        // TechnicallyQualified IS the threshold comparison §4.1 points at - it is what consolidation
        // computes from the criteria's own thresholds, so this reads the result rather than
        // re-deriving a rule.
        //
        // Proposals that do NOT pass are left in UnderReview, not moved to NotSelected. §4.1 puts
        // NotSelected under award.recommend ("Award decided for another / fails threshold"), which is
        // a later decision by a person; marking them here would pre-empt it.
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

        // §3.3 "EvaluatorSubmitted -> Consolidated | In-app to committee".
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
