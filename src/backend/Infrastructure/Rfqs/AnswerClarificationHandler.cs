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

/// <summary>FEAT-10.2/FR-CLR-002, OQ-008 interim (private-by-default, explicit publish available):
/// command.Publish defaults to false at the API layer. Notifies only the asker - a
/// PublishedToAll answer additionally notifies every other invited supplier via
/// PublishClarificationHandler below (answering-and-publishing-at-once still only reaches the
/// asker at answer time here; the visibility flip's own notification covers the rest, kept as one
/// notification per actual visibility change rather than double-emailing on the combined
/// action).</summary>
public sealed class AnswerClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : IAnswerClarificationHandler
{
    public async Task<RfqMutationResult> HandleAsync(AnswerClarificationCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        Clarification clarification;
        try
        {
            rfq.AnswerClarification(command.ClarificationId, command.Answer);
            clarification = rfq.Clarifications.Single(c => c.Id == command.ClarificationId);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_clarification_answered", scope.UserId, referenceCode: rfq.ReferenceCode,
            changes: $"{{\"clarificationId\":\"{command.ClarificationId}\",\"published\":true}}", ct: ct);
        await db.SaveChangesAsync(ct);

        // A-4: the asker is told their own question was answered; every OTHER invitee is told an
        // answer was published. Two different messages because they are two different facts, and the
        // second one must not name the asker.
        await NotifyAskerAsync(db, backgroundJobs, clarification.AskedBySupplierId, rfq.Id, clarification.Id, ct);
        await NotifyOtherInviteesAsync(db, backgroundJobs, rfq, clarification.AskedBySupplierId, ct);

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }

    internal static async Task NotifyAskerAsync(AppDbContext db, IBackgroundJobClient backgroundJobs, Guid supplierId, Guid rfqId, Guid clarificationId, CancellationToken ct)
    {
        var userId = await db.Users.Where(u => u.SupplierId == supplierId).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (userId is not null)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendClarificationAnsweredEmailAsync(userId.Value, rfqId, clarificationId, CancellationToken.None));
        }
    }

    internal static async Task NotifyOtherInviteesAsync(AppDbContext db, IBackgroundJobClient backgroundJobs, Rfq rfq, Guid excludeSupplierId, CancellationToken ct)
    {
        var otherSupplierIds = rfq.Invitations.Select(i => i.SupplierId).Where(id => id != excludeSupplierId).ToList();
        if (otherSupplierIds.Count == 0) return;

        var userIds = await db.Users.Where(u => u.SupplierId != null && otherSupplierIds.Contains(u.SupplierId.Value))
            .Select(u => u.Id).ToListAsync(ct);
        foreach (var userId in userIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendClarificationPublishedEmailAsync(userId, rfq.Id, CancellationToken.None));
        }
    }
}
