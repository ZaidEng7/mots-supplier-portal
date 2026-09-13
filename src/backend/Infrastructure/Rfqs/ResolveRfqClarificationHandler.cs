using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>T3-36. §3.1: "Clarification | UnderEvaluation | Clarification resolved".</summary>
public sealed class ResolveRfqClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IResolveRfqClarificationHandler
{
    public async Task<RfqMutationResult> HandleAsync(ResolveRfqClarificationCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.ResolveClarification();
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex, RfqState.UnderEvaluation);
        }

        // §3.1: "In-app to committee".
        NotificationOutbox.EnqueueMany(db, NotificationTypes.RfqClarificationResolved,
            await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
            $"{NotificationTypes.RfqClarificationResolved}:{rfq.Id}:{DateTimeOffset.UtcNow.Ticks}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["rfqId"] = rfq.Id.ToString() });

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_clarification_resolved", scope.UserId,
            referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.Clarification),
            toState: nameof(RfqState.UnderEvaluation), ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}
