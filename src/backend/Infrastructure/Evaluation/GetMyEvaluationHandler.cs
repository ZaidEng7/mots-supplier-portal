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

/// <summary>The blind-scoring read path (OQ-005/BRULE-058): filters every EvaluatorScore to
/// `EvaluatorUserId == scope.UserId` before it ever leaves the handler - see
/// EvaluationDtoMapper.ToMyDto's own filter.</summary>
public sealed class GetMyEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IGetMyEvaluationHandler
{
    public async Task<MyEvaluationResult> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return new MyEvaluationResult.NotFoundOrNotAssigned();
        var (rfq, evaluation) = loaded.Value;

        // EPIC-13/FEAT-13.3 audit finding: this GET was a real state mutation (Assigned/NotStarted
        // -> InProgress, "the first evaluator to open" per BUSINESS-PROCESSES.md §5.1) with zero
        // audit logging - IAuditLogger wasn't even injected. fromState captured before the call so
        // the audit row is only written when a transition genuinely happened, not on every
        // subsequent GET once already InProgress.
        var fromState = evaluation.State;
        // Opening scoring is a transition, and a READ must not be refused because the transition is no
        // longer available. Once this evaluator had submitted, the evaluation sat at EvaluatorSubmitted,
        // OpenScoring threw, and the GET answered 400 - so an evaluator could not look at the scores they
        // had just submitted, and the dashboard's own "View evaluation" link led nowhere. The same refusal
        // closed the post-consolidation window this file's own comments describe, where bidder names are
        // revealed: unreachable, because the read threw before reaching it.
        //
        // The transition still happens on exactly the states it was written for. Everything else reads.
        if (evaluation.State is EvaluationState.Assigned or EvaluationState.InProgress)
        {
            try
            {
                evaluation.OpenScoring(scope.UserId!.Value);
            }
            catch (DomainException ex)
            {
                return new MyEvaluationResult.InvalidState(ex.Message);
            }
        }
        if (evaluation.State != fromState)
        {
            await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation.scoring_started", scope.UserId,
                referenceCode: rfq.ReferenceCode, fromState: fromState.ToString(), toState: evaluation.State.ToString(), ct: ct);
        }
        await db.SaveChangesAsync(ct);

        // T-067: the RFQ's own items and requirements, so an evaluator can read the specification
        // the bids answer. Loaded explicitly because LoadScopedByAssignmentAsync fetches the Rfq bare.
        await db.Entry(rfq).Collection(r => r.Items).LoadAsync(ct);
        await db.Entry(rfq).Collection(r => r.Requirements).LoadAsync(ct);

        var bids = await EvaluationLoader.EvaluatorBidsAsync(db, rfq.Id, ct);
        return new MyEvaluationResult.Success(EvaluationDtoMapper.ToMyDto(evaluation, rfq, scope.UserId!.Value, bids));
    }
}
