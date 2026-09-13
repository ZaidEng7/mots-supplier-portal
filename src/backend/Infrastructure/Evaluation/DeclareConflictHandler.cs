// An evaluator declares whether they have a conflict of interest.
//
// A declared conflict recuses them with their stated reason, reusing the recusal the domain and the audit trail
// already have. No conflict simply closes the window.
//
// The reason is mandatory for a self-recusal for the same purpose it is when a manager recuses somebody: an
// unexplained withdrawal from a committee is not an audit record.

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
