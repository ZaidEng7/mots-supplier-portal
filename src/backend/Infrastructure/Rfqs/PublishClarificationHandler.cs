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

/// <summary>FEAT-10.2/FR-CLR-002: promotes a privately-answered clarification to PublishedToAll -
/// notifies every OTHER invited supplier (the asker already knows their own answer).</summary>
public sealed class PublishClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : IPublishClarificationHandler
{
    public async Task<RfqMutationResult> HandleAsync(PublishClarificationCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        Clarification clarification;
        try
        {
            rfq.PublishClarification(command.ClarificationId);
            clarification = rfq.Clarifications.Single(c => c.Id == command.ClarificationId);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_clarification_published", scope.UserId,
            referenceCode: rfq.ReferenceCode, changes: $"{{\"clarificationId\":\"{command.ClarificationId}\"}}", ct: ct);
        await db.SaveChangesAsync(ct);

        await AnswerClarificationHandler.NotifyOtherInviteesAsync(db, backgroundJobs, rfq, clarification.AskedBySupplierId, ct);

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}
