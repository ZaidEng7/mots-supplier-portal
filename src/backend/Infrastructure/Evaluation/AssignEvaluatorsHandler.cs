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

        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq, await EvaluationProposalCodes.ForRfqAsync(db, rfq.Id, ct)));
    }
}
