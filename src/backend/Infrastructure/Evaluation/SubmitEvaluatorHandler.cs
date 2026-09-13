// An evaluator submits their completed scoresheet.
//
// The officer is told only when every evaluator is in. That condition is part of the written rule rather than
// an optimisation: telling the officer to consolidate while two evaluators are still scoring is a false
// prompt.
//
// It goes to the tender's owner, who is the one who consolidates.

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

        if (evaluation.State == EvaluationState.EvaluatorSubmitted)
        {
            NotificationOutbox.EnqueueMany(db, NotificationTypes.EvaluatorSubmitted,
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
