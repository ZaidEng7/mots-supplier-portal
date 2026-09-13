// A manager recuses an evaluator, which is also the tool for one who never responded.
//
// The evaluation's own header explains why there is no separate quorum or exclusion action.
//
// The written process has no row for a recusal, because it is an event within a state rather than a
// transition. Notifying the officer who owns the tender is the defensible reading, and it is flagged as such
// in the notification catalogue rather than presented as transcribed.
//
// Ownership is what makes "the officer" a person: a recusal leaves the committee short an evaluator, and
// somebody has to replace them.

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
