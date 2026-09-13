// An evaluator records one score, for one bid, against one criterion.
//
// This is where the two-envelope gate is enforced; the aggregate's own method holds the rule.
//
// It loads the bids through the same projection the read uses, so a bid this evaluator cannot SEE is a bid
// they cannot SCORE. One source of truth for which bids are in play.
//
// The public bid code resolves to an internal identifier here, at the boundary. An unknown code and a code
// belonging to a different tender are the same miss, and the domain's own check on which bids are valid still
// runs behind this: two independent refusals rather than one.
//
// New score rows are forced to the inserted state explicitly rather than left to the change tracker's
// heuristic, for the reason the assignment handler's header explains.
//
// The score is formatted culture-invariantly into the audit row. That string is persisted on an append-only
// row and exported verbatim, and the process-wide culture pin is a default a thread can still override, so it
// is qualified here as well.

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

public sealed class ScoreCriterionHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IScoreCriterionHandler
{
    public async Task<MyEvaluationResult> HandleAsync(ScoreCriterionCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new MyEvaluationResult.NotFoundOrNotAssigned();
        var (rfq, evaluation) = loaded.Value;

        await db.Entry(rfq).Collection(r => r.Items).LoadAsync(ct);
        await db.Entry(rfq).Collection(r => r.Requirements).LoadAsync(ct);
        var bids = await EvaluationLoader.EvaluatorBidsAsync(db, rfq.Id, ct);

        var target = bids.FirstOrDefault(b => b.ProposalCode == command.ProposalCode);
        if (target is null) return new MyEvaluationResult.NotFoundOrNotAssigned();

        var proposalIds = bids.Select(b => b.ProposalId).ToList();
        var existingScoreIds = evaluation.Scores.Select(s => s.Id).ToHashSet();
        try
        {
            evaluation.ScoreCriterion(scope.UserId!.Value, target.ProposalId, command.CriterionId, command.RawScore, command.CommentAr, command.CommentEn, proposalIds.ToHashSet());
        }
        catch (DomainException ex)
        {
            return new MyEvaluationResult.InvalidState(ex.Message);
        }
        foreach (var score in evaluation.Scores.Where(s => !existingScoreIds.Contains(s.Id)))
        {
            db.Entry(score).State = EntityState.Added;
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation.score", scope.UserId, referenceCode: rfq.ReferenceCode,
            toState: $"{command.ProposalCode}/{command.CriterionId}={command.RawScore.ToString(CultureInfo.InvariantCulture)}", ct: ct);
        await db.SaveChangesAsync(ct);
        return new MyEvaluationResult.Success(EvaluationDtoMapper.ToMyDto(evaluation, rfq, scope.UserId!.Value, bids));
    }
}
