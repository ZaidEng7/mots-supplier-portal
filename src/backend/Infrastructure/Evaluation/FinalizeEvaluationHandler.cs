// A manager finalises the evaluation, which is the real exit from this phase.
//
// Finalising unlocks shortlisting and the award recommendation on the tender.
//
//
// THE GUARD THE WRITTEN PROCESS NAMED AND NOBODY ENFORCED
//
// The process requires the result to be reviewed with no unresolved clarification, and nothing anywhere
// checked the second half. The aggregate's own header had flagged it as a known, deliberate gap left for
// whichever piece of work could reach across to clarifications.
//
// A clarification is a child of the tender rather than its own aggregate, so this is a plain count query of
// the same shape as every other cross-aggregate guard here.

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

public sealed class FinalizeEvaluationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IFinalizeEvaluationHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(FinalizeEvaluationCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

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

        NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluationFinalized,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.EvaluationFinalized}:{evaluation.Id}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["evaluationId"] = evaluation.Id.ToString() });

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_finalized", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.Finalized), ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq, await EvaluationProposalCodes.ForRfqAsync(db, rfq.Id, ct)));
    }
}
