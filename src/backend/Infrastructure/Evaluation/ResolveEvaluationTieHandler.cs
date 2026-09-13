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
/// A-1/BRULE-069: resolves a tie that survived every tie-break rung.
///
/// <para>`evaluation.consolidate` rather than a new permission: this is the same act as producing the
/// ranking - the officer who consolidated is the one who can see the tie and is accountable for the
/// order. A new permission would be a new thing to grant on every deployment for no additional
/// separation.</para>
/// </summary>
public sealed class ResolveEvaluationTieHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IResolveEvaluationTieHandler
{
    public async Task<EvaluationMutationResult> HandleAsync(ResolveEvaluationTieCommand command, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();
        var (rfq, evaluation) = loaded.Value;

        // The public code resolves to the internal id here, inside the boundary, and only within this
        // RFQ - so a code from another tender cannot address this evaluation's results.
        var proposalId = await db.Proposals
            .Where(p => p.RfqId == rfq.Id && p.ReferenceCode == command.ProposalCode)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (proposalId is null) return new EvaluationMutationResult.NotFoundOrOutOfScope();

        try
        {
            evaluation.ResolveTie(proposalId.Value, scope.UserId!.Value, command.Reason);
        }
        catch (DomainException ex)
        {
            return new EvaluationMutationResult.InvalidState(ex.Message);
        }

        // The reason IS the record here - a tie broken by a person with no stated basis is exactly what
        // A-1 refuses to let the SYSTEM do, so it must not be what the person does either.
        await auditLogger.LogAsync("Evaluation", evaluation.Id, "evaluation_tie_resolved", scope.UserId,
            referenceCode: rfq.ReferenceCode, reason: command.Reason,
            changes: $"{{\"proposalCode\":\"{command.ProposalCode}\"}}", ct: ct);
        await db.SaveChangesAsync(ct);

        return new EvaluationMutationResult.Success(EvaluationDtoMapper.ToDto(evaluation, rfq, await EvaluationProposalCodes.ForRfqAsync(db, rfq.Id, ct)));
    }
}
