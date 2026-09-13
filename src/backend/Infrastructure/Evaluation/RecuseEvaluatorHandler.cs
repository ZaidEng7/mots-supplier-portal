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

        // §3.3 has no row for recusal - it is a within-state event, not a transition. Notifying the
        // officer who owns the RFQ is the defensible reading, and it is flagged as such in the
        // catalogue rather than presented as transcribed. A-7 makes "the officer" a person: a
        // recusal leaves the evaluation short an evaluator, and somebody has to replace them.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluatorRecused,
            await NotificationRecipients.RfqOwnerAsync(db, rfq, ct),
            $"{NotificationTypes.EvaluatorRecused}:{evaluation.Id}:{command.EvaluatorUserId}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["evaluationId"] = evaluation.Id.ToString() });

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_evaluator_recused", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: command.EvaluatorUserId.ToString(), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq, await EvaluationProposalCodes.ForRfqAsync(db, rfq.Id, ct)));
    }
}
