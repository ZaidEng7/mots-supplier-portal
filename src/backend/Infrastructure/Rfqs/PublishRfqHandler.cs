// Publishing an approved tender, which is what makes it visible to its invitees.
//
// Every currently invited supplier's state is re-checked first. Inviting them only guaranteed they were
// active AT INVITE TIME, and time may have passed and a supplier may have been suspended or deactivated
// between the invitation and this moment.
//
// Every invited supplier is then told that submissions are open. Before this, only the invitation email told
// them a tender existed at all, and nothing marked the moment it actually opened.

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

public sealed class PublishRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IPublishRfqHandler
{
    public async Task<RfqMutationResult> HandleAsync(PublishRfqCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        var invitedSupplierIds = rfq.Invitations.Select(i => i.SupplierId).ToList();
        if (invitedSupplierIds.Count > 0)
        {
            var inactiveCount = await db.Suppliers
                .Where(s => invitedSupplierIds.Contains(s.Id) && s.LifecycleState != SupplierLifecycleState.Active)
                .CountAsync(ct);
            if (inactiveCount > 0)
            {
                return new RfqMutationResult.SupplierNotActive();
            }
        }

        try
        {
            rfq.Publish();
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex, RfqState.Published);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_published", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(RfqState.Approved), toState: nameof(RfqState.Published), ct: ct);
        await db.SaveChangesAsync(ct);

        if (invitedSupplierIds.Count > 0)
        {
            var userIds = await db.Users.Where(u => u.SupplierId != null && invitedSupplierIds.Contains(u.SupplierId.Value))
                .Select(u => u.Id).ToListAsync(ct);
            foreach (var userId in userIds)
            {
                backgroundJobs.Enqueue<EmailJobs>(job => job.SendRfqPublishedEmailAsync(userId, rfq.Id, CancellationToken.None));
            }
        }

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}
