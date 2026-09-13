// An evaluator's own assignments, their due dates and how far through each one they are.
//
//
// SCOPED BY ASSIGNMENT, NOT BY ORGANIZATION
//
// For the reason the shared loader gives: an evaluator need not belong to the procuring organization, and may
// have no organization at all.
//
// A widget scoped the usual way would show them nothing, which is precisely the tender-stopper this screen
// exists to fix, reintroduced one layer down.
//
// Recused assignments are excluded. A recused evaluator is no longer being asked for anything, and leaving
// the row on their dashboard would be asking.
//
//
// PROGRESS IS COUNTED PER EVALUATION, NOT PER ROW
//
// Three grouped queries rather than two per assignment. A dashboard that issues a query per assignment is
// fine with three assignments and a problem with thirty, and nothing about the screen tells you which you
// have.
//
//
// THE THREE TABS ARE DERIVED
//
// Submitted comes from the assignment's own submission, not from the evaluation's state, which belongs to the
// whole committee: an evaluator who has submitted is done even while three colleagues are still scoring.
//
// In progress means at least one score recorded. Assigned is everything else.

namespace MotsSupplierPortal.Infrastructure.Evaluation;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ListMyAssignmentsHandler(AppDbContext db, IScopeContext scope) : IListMyAssignmentsHandler
{
    public async Task<IReadOnlyList<MyAssignmentDto>> HandleAsync(string? tab, CancellationToken ct)
    {
        if (scope.UserId is not { } userId) return [];

        var rows = await (
            from assignment in db.EvaluationAssignments
            join evaluation in db.Evaluations on assignment.EvaluationId equals evaluation.Id
            join rfq in db.Rfqs on evaluation.RfqId equals rfq.Id
            where assignment.EvaluatorUserId == userId && assignment.RecusedAt == null
            select new
            {
                rfq.ReferenceCode,
                rfq.TitleAr,
                rfq.TitleEn,
                rfq.EvaluationTargetDate,
                EvaluationId = evaluation.Id,
                EvaluationState = evaluation.State,
                RfqId = rfq.Id,
                assignment.AssignedAt,
                assignment.SubmittedAt,
            }).ToListAsync(ct);

        if (rows.Count == 0) return [];

        var evaluationIds = rows.Select(r => r.EvaluationId).ToList();
        var rfqIds = rows.Select(r => r.RfqId).ToList();

        var scoreCounts = await db.EvaluatorScores
            .Where(s => s.EvaluatorUserId == userId && evaluationIds.Contains(s.EvaluationId))
            .GroupBy(s => s.EvaluationId)
            .Select(g => new { EvaluationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.EvaluationId, g => g.Count, ct);

        var criterionCounts = await db.EvaluationCriterionSnapshots
            .Where(c => evaluationIds.Contains(c.EvaluationId))
            .GroupBy(c => c.EvaluationId)
            .Select(g => new { EvaluationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.EvaluationId, g => g.Count, ct);

        var proposalCounts = await db.Proposals
            .Where(p => rfqIds.Contains(p.RfqId) && ProposalStates.InEvaluation.Contains(p.State))
            .GroupBy(p => p.RfqId)
            .Select(g => new { RfqId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RfqId, g => g.Count, ct);

        var assignments = rows.Select(row =>
        {
            var recorded = scoreCounts.GetValueOrDefault(row.EvaluationId);
            var expected = criterionCounts.GetValueOrDefault(row.EvaluationId)
                           * proposalCounts.GetValueOrDefault(row.RfqId);

            return new MyAssignmentDto(
                row.ReferenceCode, row.TitleAr, row.TitleEn, row.EvaluationState.ToString(),
                row.EvaluationTargetDate, row.AssignedAt, row.SubmittedAt,
                recorded, expected, TabFor(row.SubmittedAt, recorded));
        }).ToList();

        return tab is null
            ? assignments
            : [.. assignments.Where(a => string.Equals(a.Tab, tab, StringComparison.OrdinalIgnoreCase))];
    }

    private static string TabFor(DateTimeOffset? submittedAt, int scoresRecorded) =>
        submittedAt is not null ? MyAssignmentTabs.Submitted
        : scoresRecorded > 0 ? MyAssignmentTabs.InProgress
        : MyAssignmentTabs.Assigned;
}
