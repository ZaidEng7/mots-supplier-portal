// An evaluator opens their own scoring workspace.
//
// Every score is narrowed to this evaluator's own before it leaves the handler, which is what blind scoring
// means here: one evaluator cannot see another's marks.
//
//
// THIS READ IS ALSO A TRANSITION, AND IT IS NOW AUDITED
//
// The first evaluator to open the workspace is what starts scoring, which the written process says plainly.
// It was a real state change with no audit row at all; the logger was not even a dependency.
//
// The prior state is captured before the call, so the audit row is written only when a transition genuinely
// happened rather than on every later read once scoring is already under way.
//
//
// A READ MUST NOT BE REFUSED BECAUSE THE TRANSITION IS NO LONGER AVAILABLE
//
// Once this evaluator had submitted, the evaluation sat one state further on, opening scoring threw, and the
// read answered with a bad request. So an evaluator could not look at the scores they had just submitted, and
// their dashboard's own link to the evaluation led nowhere.
//
// The same refusal also closed the post-consolidation window in which bidder names are revealed: unreachable,
// because the read threw before it got there.
//
// The transition still happens on exactly the two states it was written for. Every other state reads.
//
// The tender's own lines and requirements are loaded explicitly, so an evaluator can read the specification
// the bids answer. The shared loader fetches the tender bare.

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

public sealed class GetMyEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IGetMyEvaluationHandler
{
    public async Task<MyEvaluationResult> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return new MyEvaluationResult.NotFoundOrNotAssigned();
        var (rfq, evaluation) = loaded.Value;

        var fromState = evaluation.State;
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

        await db.Entry(rfq).Collection(r => r.Items).LoadAsync(ct);
        await db.Entry(rfq).Collection(r => r.Requirements).LoadAsync(ct);

        var bids = await EvaluationLoader.EvaluatorBidsAsync(db, rfq.Id, ct);
        return new MyEvaluationResult.Success(EvaluationDtoMapper.ToMyDto(evaluation, rfq, scope.UserId!.Value, bids));
    }
}
