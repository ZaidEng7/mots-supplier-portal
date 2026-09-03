using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluations;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

namespace MotsSupplierPortal.Infrastructure.Evaluations;

internal static class EvaluationDtoMapper
{
    public static EvaluationDto ToDto(EvaluationAggregate evaluation, Rfq rfq) => new(
        evaluation.Id, evaluation.RfqId, rfq.ReferenceCode, evaluation.State,
        [.. evaluation.Criteria.Select(ToCriterionDto)],
        [.. evaluation.Assignments.Select(a => new EvaluationAssignmentDto(a.EvaluatorUserId, a.AssignedAt, a.SubmittedAt, a.RecusedAt, a.RecusalReason))],
        [.. evaluation.Results.Select(r => new ConsolidatedResultDto(r.ProposalId, r.TechnicallyQualified, r.TechnicalWeightedScore, r.FinancialWeightedScore, r.WeightedTotal, r.Rank))],
        evaluation.RowVersion);

    public static EvaluationCriterionDto ToCriterionDto(EvaluationCriterionSnapshot c) =>
        new(c.Id, c.NameAr, c.NameEn, c.Dimension, c.Weight, c.MaxScore, c.Threshold, c.ScoringType, c.IsFinancial);

    public static MyEvaluationDto ToMyDto(EvaluationAggregate evaluation, Rfq rfq, Guid evaluatorUserId, IReadOnlyList<Guid> proposalIds)
    {
        var assignment = evaluation.Assignments.First(a => a.EvaluatorUserId == evaluatorUserId && a.IsActive);
        var qualification = proposalIds.ToDictionary(p => p, p => evaluation.IsTechnicallyQualifiedByEvaluator(evaluatorUserId, p));
        var myScores = evaluation.Scores.Where(s => s.EvaluatorUserId == evaluatorUserId)
            .Select(s => new MyScoreDto(s.ProposalId, s.CriterionId, s.RawScore, s.CommentAr, s.CommentEn, s.ScoredAt))
            .ToList();
        return new MyEvaluationDto(
            evaluation.Id, evaluation.RfqId, rfq.ReferenceCode, evaluation.State,
            assignment.SubmittedAt, [.. evaluation.Criteria.Select(ToCriterionDto)], proposalIds, qualification, myScores);
    }
}

file static class EvaluationLoader
{
    public static IQueryable<EvaluationAggregate> IncludeAll(this DbSet<EvaluationAggregate> set) =>
        set.Include(e => e.Criteria).Include(e => e.Assignments).Include(e => e.Scores).Include(e => e.Results).AsSplitQuery();

    /// <summary>Buyer-side: scoped to the caller's own Organization via the bound Rfq, same shape
    /// as RfqLoader.LoadScopedAsync.</summary>
    public static async Task<(Rfq Rfq, EvaluationAggregate Evaluation)?> LoadScopedByOrgAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return null;
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return null;
        var evaluation = await db.Evaluations.IncludeAll().FirstOrDefaultAsync(e => e.RfqId == rfq.Id, ct);
        return evaluation is null ? null : (rfq, evaluation);
    }

    /// <summary>Evaluator-side: scoped to "this caller holds an active assignment on this
    /// evaluation" - deliberately NOT to OrganizationId (an evaluator need not belong to the
    /// procuring organization).</summary>
    public static async Task<(Rfq Rfq, EvaluationAggregate Evaluation)?> LoadScopedByAssignmentAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.UserId is null) return null;
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode, ct);
        if (rfq is null) return null;
        var evaluation = await db.Evaluations.IncludeAll().FirstOrDefaultAsync(e => e.RfqId == rfq.Id, ct);
        if (evaluation is null) return null;
        if (!evaluation.Assignments.Any(a => a.EvaluatorUserId == scope.UserId && a.IsActive)) return null;
        return (rfq, evaluation);
    }

    public static Task<List<Guid>> SubmittedProposalIdsAsync(AppDbContext db, Guid rfqId, CancellationToken ct) =>
        db.Proposals.Where(p => p.RfqId == rfqId && p.State == ProposalState.Submitted).Select(p => p.Id).ToListAsync(ct);
}

/// <summary>FEAT-11.2/FR-EVL-001, BUSINESS-PROCESSES.md §5.1: "SubmissionClosed -&gt;
/// UnderEvaluation ... system (on RFQ UnderEvaluation) ... Instantiate criteria from
/// EvaluationTemplate; snapshot weights". The RFQ's own SubmissionClosed -&gt; UnderEvaluation
/// transition and the Evaluation's creation happen in the same handler/SaveChangesAsync call -
/// same pragmatic single-unit-of-work exception BindEvaluationTemplateHandler's own doc comment
/// already justifies, not a new pattern.</summary>
public sealed class OpenEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IOpenEvaluationHandler
{
    private sealed record CriterionSnapshotJson(Guid Id, string NameAr, string NameEn, string Dimension, decimal Weight, decimal MaxScore, decimal? Threshold, string ScoringType);

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

        var criteriaJson = JsonSerializer.Deserialize<List<CriterionSnapshotJson>>(rfq.EvaluationTemplateSnapshotJson)!;
        var criteriaInputs = criteriaJson.Select(c => new CriterionSnapshotInput(
            c.NameAr, c.NameEn, Enum.Parse<CriterionDimension>(c.Dimension), c.Weight, c.MaxScore, c.Threshold, Enum.Parse<ScoringType>(c.ScoringType))).ToList();

        var evaluation = EvaluationAggregate.Create(rfq.Id, criteriaInputs);
        db.Evaluations.Add(evaluation);

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_evaluation_opened", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: fromState.ToString(), toState: nameof(RfqState.UnderEvaluation), ct: ct);
        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_created", scope.UserId, referenceCode: rfq.ReferenceCode,
            toState: nameof(EvaluationState.NotStarted), ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

public sealed class GetEvaluationHandler(AppDbContext db, IScopeContext scope) : IGetEvaluationHandler
{
    public async Task<EvaluationDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, rfqReferenceCode, ct);
        return loaded is null ? null : EvaluationDtoMapper.ToDto(loaded.Value.Evaluation, loaded.Value.Rfq);
    }
}

/// <summary>FEAT-11.2/FR-EVL-001, BUSINESS-PROCESSES.md §5.1: "— -&gt; Assigned ... procurement_manager
/// / evaluation.assign". Assigns every candidate to every Submitted proposal on the RFQ - see
/// EvaluationAssignment.cs's own doc comment on why no per-evaluator proposal subset exists.</summary>
/// <summary>FEAT-13.3 audit gap fix: notifies each newly-assigned evaluator - previously they only
/// learned of the assignment by independently checking their own evaluation dashboard.</summary>
public sealed class AssignEvaluatorsHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IAssignEvaluatorsHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(AssignEvaluatorsCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        var existingAssignmentIds = evaluation.Assignments.Select(a => a.Id).ToHashSet();
        try
        {
            evaluation.AssignEvaluators(command.EvaluatorUserIds);
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }
        // EF's change-tracker misclassifies a brand-new child appended to an already-Included
        // collection as Modified (not Added) when the SAME SaveChanges also updates the owning
        // Evaluation row's own State column (NotStarted -&gt; Assigned here) - forcing the state
        // explicitly for the rows this call actually created sidesteps that misdetection rather
        // than relying on DetectChanges' fixup heuristic to get it right.
        foreach (var assignment in evaluation.Assignments.Where(a => !existingAssignmentIds.Contains(a.Id)))
        {
            db.Entry(assignment).State = EntityState.Added;
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_evaluators_assigned", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: string.Join(",", command.EvaluatorUserIds), ct: ct);
        await db.SaveChangesAsync(ct);

        foreach (var evaluatorUserId in command.EvaluatorUserIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendEvaluatorAssignedEmailAsync(evaluatorUserId, rfq.Id, CancellationToken.None));
        }

        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

/// <summary>BRULE-067, and FEAT-11.7/FR-EVL-011's non-responding-evaluator tool (see
/// Evaluation.cs's own class doc comment on why no separate quorum/exclude action exists).</summary>
public sealed class RecuseEvaluatorHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRecuseEvaluatorHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(RecuseEvaluatorCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        try
        {
            evaluation.RecuseEvaluator(command.EvaluatorUserId, command.Reason);
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_evaluator_recused", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: command.EvaluatorUserId.ToString(), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

/// <summary>FEAT-11.6/FR-EVL-007, procurement_officer,procurement_manager / evaluation.consolidate.</summary>
public sealed class ConsolidateEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IConsolidateEvaluationHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(ConsolidateEvaluationCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        var existingResultIds = evaluation.Results.Select(r => r.Id).ToHashSet();
        try
        {
            evaluation.Consolidate();
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

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_consolidated", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.Consolidated), ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

/// <summary>FEAT-11.6/FR-EVL-008, procurement_manager / evaluation.finalize - the real Phase 7 exit
/// gate: unlocks RFQ shortlisting/recommendation (BUSINESS-PROCESSES.md §3), though this build does
/// not implement that next RFQ transition itself (EPIC-13/14 territory) - see class doc comment on
/// EvaluationAggregate.FinalizeEvaluation.</summary>
public sealed class FinalizeEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IFinalizeEvaluationHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(FinalizeEvaluationCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        // EPIC-13/FEAT-13.2 stage-gate audit: BUSINESS-PROCESSES.md §5.1's own "Result reviewed;
        // no unresolved clarification" guard for this transition was never enforced anywhere -
        // Evaluation.FinalizeEvaluation's own doc comment already flagged this as a known,
        // deliberate gap left for whichever epic could reach across to Clarification (this one).
        // Clarification is a child entity of Rfq (Domain/Rfqs/Clarification.cs), not its own
        // aggregate, so this is a plain cross-aggregate-adjacent count query, same shape as every
        // other cross-aggregate guard in this codebase.
        var unresolvedClarifications = await db.Clarifications.CountAsync(c => c.RfqId == rfq.Id && c.Answer == null, ct);
        if (unresolvedClarifications > 0)
        {
            return new EvaluationMutationResult.InvalidState(
                $"Cannot finalize the evaluation: {unresolvedClarifications} clarification question(s) are still unanswered.");
        }

        try
        {
            evaluation.FinalizeEvaluation();
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_finalized", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.Finalized), ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

public sealed class ReopenEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IReopenEvaluationHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(ReopenEvaluationCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        try
        {
            evaluation.ReopenForClarification(command.Reason);
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_reopened", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.InProgress), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq));
    }
}

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
        try
        {
            evaluation.OpenScoring(scope.UserId!.Value);
        }
        catch (DomainException ex)
        {
            return new MyEvaluationResult.InvalidState(ex.Message);
        }
        if (evaluation.State != fromState)
        {
            await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation.scoring_started", scope.UserId,
                referenceCode: rfq.ReferenceCode, fromState: fromState.ToString(), toState: evaluation.State.ToString(), ct: ct);
        }
        await db.SaveChangesAsync(ct);

        var proposalIds = await EvaluationLoader.SubmittedProposalIdsAsync(db, rfq.Id, ct);
        return new MyEvaluationResult.Success(EvaluationDtoMapper.ToMyDto(evaluation, rfq, scope.UserId!.Value, proposalIds));
    }
}

/// <summary>FEAT-11.3/FR-EVL-003/004/005 - the two-envelope gate's enforcement point, see
/// EvaluationAggregate.ScoreCriterion's own doc comment.</summary>
public sealed class ScoreCriterionHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IScoreCriterionHandler
{
    public async Task<MyEvaluationResult> HandleAsync(ScoreCriterionCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new MyEvaluationResult.NotFoundOrNotAssigned();
        var (rfq, evaluation) = loaded.Value;

        var proposalIds = await EvaluationLoader.SubmittedProposalIdsAsync(db, rfq.Id, ct);
        var existingScoreIds = evaluation.Scores.Select(s => s.Id).ToHashSet();
        try
        {
            evaluation.ScoreCriterion(scope.UserId!.Value, command.ProposalId, command.CriterionId, command.RawScore, command.CommentAr, command.CommentEn, proposalIds.ToHashSet());
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
            toState: $"{command.ProposalId}/{command.CriterionId}={command.RawScore}", ct: ct);
        await db.SaveChangesAsync(ct);
        return new MyEvaluationResult.Success(EvaluationDtoMapper.ToMyDto(evaluation, rfq, scope.UserId!.Value, proposalIds));
    }
}

public sealed class SubmitEvaluatorHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISubmitEvaluatorHandler
{
    public async Task<MyEvaluationResult> HandleAsync(SubmitEvaluatorCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new MyEvaluationResult.NotFoundOrNotAssigned();
        var (rfq, evaluation) = loaded.Value;

        var proposalIds = await EvaluationLoader.SubmittedProposalIdsAsync(db, rfq.Id, ct);
        try
        {
            evaluation.SubmitEvaluator(scope.UserId!.Value, proposalIds.ToHashSet());
        }
        catch (DomainException ex)
        {
            return new MyEvaluationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_evaluator_submitted", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.EvaluatorSubmitted), ct: ct);
        await db.SaveChangesAsync(ct);
        return new MyEvaluationResult.Success(EvaluationDtoMapper.ToMyDto(evaluation, rfq, scope.UserId!.Value, proposalIds));
    }
}
