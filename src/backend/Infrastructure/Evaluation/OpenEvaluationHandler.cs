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

/// <summary>FEAT-11.2/FR-EVL-001, BUSINESS-PROCESSES.md §5.1: "SubmissionClosed -&gt;
/// UnderEvaluation ... system (on RFQ UnderEvaluation) ... Instantiate criteria from
/// EvaluationTemplate; snapshot weights". The RFQ's own SubmissionClosed -&gt; UnderEvaluation
/// transition and the Evaluation's creation happen in the same handler/SaveChangesAsync call -
/// same pragmatic single-unit-of-work exception BindEvaluationTemplateHandler's own doc comment
/// already justifies, not a new pattern.</summary>
public sealed class OpenEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IOpenEvaluationHandler
{
    private sealed record CriterionSnapshotJson(
        Guid Id, string NameAr, string NameEn, string Dimension, decimal Weight, decimal MaxScore, decimal? Threshold,
        string ScoringType,
        // Defaults to false for an RFQ whose snapshot predates the field - the same reason Criterion's
        // own flag defaults false rather than being backfilled from a rule nobody had stated yet.
        bool RequiresJustification = false,
        // SCR-501. Null for a tender whose snapshot predates the field, which is the honest answer: the
        // guidance was not recorded when that RFQ bound its template, and inventing it now from the
        // template's current text would show an evaluator an instruction this tender never carried.
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
        // BUSINESS-PROCESSES.md §5.1 guard "&gt;=1 Submitted proposal [ASSUMPTION]" - cross-aggregate
        // (Proposal lives in a different aggregate), resolved here before the domain transition,
        // same split as every other cross-aggregate guard in this codebase.
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

        // T-051, FR-PRP-009, §4.1: "Submitted -> UnderReview | Evaluation opened | system (on RFQ
        // UnderEvaluation) | Make visible to assigned evaluators (scoped)".
        //
        // This is the gateway the whole middle of the proposal lifecycle hung on. Nothing assigned
        // UnderReview, so ClarificationRequested, Revised and Shortlisted were unreachable too - a
        // proposal went Draft -> Submitted -> outcome and skipped evaluation intake entirely.
        //
        // In the SAME SaveChanges as the RFQ's own transition, because a window where the RFQ is
        // UnderEvaluation and its proposals are still Submitted is a state no document describes.
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

        // §3.1 "SubmissionClosed -> UnderEvaluation | In-app to `evaluator`s". No assignments exist
        // yet at this moment, so this is the committee that runs the RFQ rather than a list of
        // assignees - the assignment notification (§3.3 "NotStarted -> Assigned") is the one that
        // reaches individual evaluators, and it already exists as an email.
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
