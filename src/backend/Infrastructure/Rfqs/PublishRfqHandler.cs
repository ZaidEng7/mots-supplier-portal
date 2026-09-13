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

/// <summary>FEAT-07.5/BUSINESS-PROCESSES.md §3.1: Approved -&gt; Published. BRULE-032/EPIC-08 gap
/// closed: re-checks every currently-invited supplier's LifecycleState before publishing, since
/// InviteSupplierHandler only guarantees Active AT INVITE time - time may have passed (and a
/// supplier may have been suspended/deactivated) between invite and this transition.
/// FEAT-13.3 audit gap fix: notifies every invited supplier that submissions are now open -
/// previously only the invite-time email told them an RFQ existed at all, with nothing marking the
/// actual open-for-submission moment.</summary>
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
