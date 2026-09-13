// A manager seats the evaluation committee.
//
// Every evaluator is assigned to every bid on the tender; the assignment record's own header explains why
// there is no per-evaluator subset of bids.
//
// Each newly assigned evaluator is emailed. Before this they only learned of the assignment by independently
// checking their own dashboard.
//
//
// WHY THE NEW ROWS ARE FORCED TO THE INSERTED STATE
//
// The change tracker misclassifies a brand-new child appended to an already-loaded collection as an existing
// row rather than a new one when the same save also updates the owning evaluation's own state column, which
// it does here.
//
// Setting the state explicitly for the rows this call actually created sidesteps that, rather than relying on
// the tracker's fixup heuristic to get it right. Several handlers in this folder point back at this
// explanation.

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
