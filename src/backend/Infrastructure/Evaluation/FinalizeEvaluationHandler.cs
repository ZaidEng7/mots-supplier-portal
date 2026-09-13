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

        // §3.3 "Consolidated -> Finalized | In-app to committee".
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
