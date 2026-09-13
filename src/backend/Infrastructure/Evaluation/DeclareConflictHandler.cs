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

/// <summary>
/// A-8/BRULE-067: the declaration itself. A conflict recuses the evaluator with their stated reason -
/// reusing the recusal the domain and the audit trail already have - and no conflict closes the window.
/// </summary>
public sealed class DeclareConflictHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IDeclareConflictHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(DeclareConflictCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        try
        {
            if (command.HasConflict)
            {
                // A self-recusal, and the reason is mandatory for the same purpose it is when a manager
                // recuses someone: an unexplained withdrawal from a committee is not an audit record.
                evaluation.RecuseEvaluator(scope.UserId!.Value, command.Reason ?? string.Empty);
            }
            else
            {
                evaluation.DeclareNoConflict(scope.UserId!.Value);
            }
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id,
            command.HasConflict ? "evaluator_self_recused" : "evaluator_declared_no_conflict",
            scope.UserId, referenceCode: rfq.ReferenceCode, reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);

        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq, await EvaluationProposalCodes.ForRfqAsync(db, rfq.Id, ct)));
    }
}
