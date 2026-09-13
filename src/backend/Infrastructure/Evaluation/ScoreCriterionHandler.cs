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

/// <summary>FEAT-11.3/FR-EVL-003/004/005 - the two-envelope gate's enforcement point, see
/// EvaluationAggregate.ScoreCriterion's own doc comment.</summary>
public sealed class ScoreCriterionHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IScoreCriterionHandler
{
    public async Task<MyEvaluationResult> HandleAsync(ScoreCriterionCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new MyEvaluationResult.NotFoundOrNotAssigned();
        var (rfq, evaluation) = loaded.Value;

        // T-067: the same projection the read uses, so a bid this evaluator cannot SEE is a bid they
        // cannot SCORE - one source of truth for which proposals are in play.
        await db.Entry(rfq).Collection(r => r.Items).LoadAsync(ct);
        await db.Entry(rfq).Collection(r => r.Requirements).LoadAsync(ct);
        var bids = await EvaluationLoader.EvaluatorBidsAsync(db, rfq.Id, ct);

        // T-068: the public code resolves to a GUID here, at the boundary. An unknown code and a code
        // belonging to a different RFQ are the same miss, and the domain's own validProposalIds guard
        // still runs behind this - two independent refusals rather than one.
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
        // See AssignEvaluatorsHandler's own comment on why this is forced explicitly rather than
        // left to DetectChanges' fixup heuristic.
        foreach (var score in evaluation.Scores.Where(s => !existingScoreIds.Contains(s.Id)))
        {
            db.Entry(score).State = EntityState.Added;
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation.score", scope.UserId, referenceCode: rfq.ReferenceCode,
            // T-048: the score is a decimal, and this string is persisted onto an append-only audit
            // row and exported verbatim to CSV. Qualified here as well as pinned at startup, because
            // the startup pin is a default a thread can still override.
            toState: $"{command.ProposalCode}/{command.CriterionId}={command.RawScore.ToString(CultureInfo.InvariantCulture)}", ct: ct);
        await db.SaveChangesAsync(ct);
        return new MyEvaluationResult.Success(EvaluationDtoMapper.ToMyDto(evaluation, rfq, scope.UserId!.Value, bids));
    }
}
