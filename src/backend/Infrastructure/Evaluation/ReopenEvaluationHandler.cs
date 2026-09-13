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

        // §3.3 "Consolidated -> InProgress | In-app to affected evaluators" - the assigned, non-recused
        // evaluators, which is what "affected" means here.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluationReopened,
            await NotificationRecipients.AssignedEvaluatorsAsync(db, evaluation.Id, ct),
            $"{NotificationTypes.EvaluationReopened}:{evaluation.Id}:{DateTimeOffset.UtcNow.Ticks}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["evaluationId"] = evaluation.Id.ToString() });

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_reopened", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.InProgress), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq, await EvaluationProposalCodes.ForRfqAsync(db, rfq.Id, ct)));
    }
}
