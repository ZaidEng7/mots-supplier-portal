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

public sealed class SubmitEvaluatorHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISubmitEvaluatorHandler
{
    public async Task<MyEvaluationResult> HandleAsync(SubmitEvaluatorCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new MyEvaluationResult.NotFoundOrNotAssigned();
        var (rfq, evaluation) = loaded.Value;

        await db.Entry(rfq).Collection(r => r.Items).LoadAsync(ct);
        await db.Entry(rfq).Collection(r => r.Requirements).LoadAsync(ct);
        var bids = await EvaluationLoader.EvaluatorBidsAsync(db, rfq.Id, ct);
        var proposalIds = bids.Select(b => b.ProposalId).ToList();
        try
        {
            evaluation.SubmitEvaluator(scope.UserId!.Value, proposalIds.ToHashSet());
        }
        catch (DomainException ex)
        {
            return new MyEvaluationResult.InvalidState(ex.Message);
        }

        // §3.3 "InProgress -> EvaluatorSubmitted | In-app to `procurement_officer` WHEN ALL IN". The
        // condition is part of the rule, not an optimisation: telling the officer to consolidate
        // while two evaluators are still scoring is a false prompt.
        if (evaluation.State == EvaluationState.EvaluatorSubmitted)
        {
            NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluatorSubmitted,
                // A-7: the owner, who is the one who consolidates.
                await NotificationRecipients.RfqOwnerAsync(db, rfq, ct),
                $"{NotificationTypes.EvaluatorSubmitted}:{evaluation.Id}",
                new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["evaluationId"] = evaluation.Id.ToString() });
        }

        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_evaluator_submitted", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: nameof(EvaluationState.EvaluatorSubmitted), ct: ct);
        await db.SaveChangesAsync(ct);
        return new MyEvaluationResult.Success(EvaluationDtoMapper.ToMyDto(evaluation, rfq, scope.UserId!.Value, bids));
    }
}
