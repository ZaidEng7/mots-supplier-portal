// A buyer answers a bidder's question.
//
// Answers are private by default and publishing to everyone is an explicit second step, which is the interim
// reading of an open question the ministry has not settled.
//
//
// TWO MESSAGES, BECAUSE THEY ARE TWO DIFFERENT FACTS
//
// The asker is told their own question was answered. Every other invitee is told that an answer was
// published, and that second message must not name the asker.
//
// Answering and publishing in one action still only reaches the asker from here. The visibility change has
// its own notification, which covers the rest, so there is one notification per actual change rather than two
// emails for the combined action.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

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
