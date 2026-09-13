// Opening evaluation on a closed tender: creating the evaluation and instantiating its criteria.
//
// The tender's own move into evaluation and the evaluation's creation happen in one commit. That is the same
// pragmatic single-unit-of-work exception the template binding already justifies, rather than a new pattern.
//
// A template must be bound, and at least one bid must have been submitted. The bid count is a cross-aggregate
// guard, resolved here before the domain transition, which is the same split every other cross-aggregate
// guard in this codebase uses.
//
//
// THIS IS THE GATEWAY THE MIDDLE OF THE BID LIFECYCLE HUNG ON
//
// Nothing moved bids into review, so the states after it, clarification requested, revised and shortlisted,
// were unreachable too. A bid went from draft to submitted to an outcome and skipped evaluation intake
// entirely.
//
// The bids move in the SAME commit as the tender, because a window in which the tender is under evaluation
// and its bids are still merely submitted is a state no document describes.
//
//
// READING A SNAPSHOT THAT PREDATES A FIELD
//
// Whether a criterion requires a justification defaults to false for a tender whose snapshot was written
// before that field existed, the same reason the criterion's own flag defaults false rather than being
// backfilled from a rule nobody had stated yet.
//
// The guidance text is absent for such a tender, which is the honest answer. It was not recorded when that
// tender bound its template, and inventing it now from the template's current text would show an evaluator an
// instruction this tender never carried.
//
//
// WHO IS NOTIFIED
//
// No assignments exist yet at this moment, so this notifies the committee that runs the tender rather than a
// list of assignees. The assignment notification is the one that reaches individual evaluators, and it
// already exists as an email.

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

public sealed class OpenEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IOpenEvaluationHandler
{
    private sealed record CriterionSnapshotJson(
        Guid Id, string NameAr, string NameEn, string Dimension, decimal Weight, decimal MaxScore, decimal? Threshold,
        string ScoringType,
        bool RequiresJustification = false,
        string? GuidanceAr = null,
        string? GuidanceEn = null);

    public async Task<EvaluationMutationResult> HandleAsync(OpenEvaluationCommand command, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == command.RfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();

        if (rfq.EvaluationTemplateSnapshotJson is null)
        {
            return new EvaluationMutationResult.InvalidState("Cannot open evaluation: no evaluation template is bound to this RFQ.");
        }
        var submittedCount = await db.Proposals.CountAsync(p => p.RfqId == rfq.Id && p.State == ProposalState.Submitted, ct);
        if (submittedCount == 0)
        {
            return new EvaluationMutationResult.InvalidState("Cannot open evaluation: at least one Submitted proposal is required.");
        }

        var fromState = rfq.State;
        try
        {
            rfq.OpenEvaluation();
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        var intake = await db.Proposals
            .Where(p => p.RfqId == rfq.Id && p.State == ProposalState.Submitted)
            .ToListAsync(ct);

        foreach (var proposal in intake)
        {
            proposal.OpenForReview();
            await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_under_review", scope.UserId,
                referenceCode: proposal.ReferenceCode,
                fromState: nameof(ProposalState.Submitted), toState: nameof(ProposalState.UnderReview), ct: ct);
        }

        var criteriaJson = JsonSerializer.Deserialize<List<CriterionSnapshotJson>>(rfq.EvaluationTemplateSnapshotJson)!;
        var criteriaInputs = criteriaJson.Select(c => new CriterionSnapshotInput(
            c.NameAr, c.NameEn, Enum.Parse<CriterionDimension>(c.Dimension), c.Weight, c.MaxScore, c.Threshold,
            Enum.Parse<ScoringType>(c.ScoringType), c.RequiresJustification, c.GuidanceAr, c.GuidanceEn)).ToList();

        var evaluation = EvaluationAggregate.Create(rfq.Id, criteriaInputs);
        db.Evaluations.Add(evaluation);

        NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluationOpened,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.EvaluationOpened}:{rfq.Id}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_evaluation_opened", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: fromState.ToString(), toState: nameof(RfqState.UnderEvaluation), ct: ct);
        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_created", scope.UserId, referenceCode: rfq.ReferenceCode,
            toState: nameof(EvaluationState.NotStarted), ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq, await EvaluationProposalCodes.ForRfqAsync(db, rfq.Id, ct)));
    }
}
