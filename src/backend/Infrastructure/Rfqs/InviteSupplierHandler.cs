// Inviting one supplier to a tender.
//
// The supplier must be active. That is checked here rather than in the domain because it crosses aggregates,
// and the tender's own method says so.
//
// The invited supplier's primary user is emailed. The email goes through the job queue rather than the
// outbox, for the reason the email jobs explain: the outbox is the road for integration events, and a
// notification email is a different road.
//
// "In-app" here means the invited supplier's own tender list reflecting the new invitation on its next fetch,
// which is what in-app means for every other transition in this codebase rather than a gap invented for this
// feature alone.

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

public sealed class InviteSupplierHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : IInviteSupplierHandler
{
    public async Task<RfqMutationResult> HandleAsync(InviteSupplierCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == command.SupplierId, ct);
        if (supplier is null || supplier.LifecycleState != SupplierLifecycleState.Active)
        {
            return new RfqMutationResult.SupplierNotActive();
        }

        Invitation invitation;
        try
        {
            invitation = rfq.InviteSupplier(command.SupplierId);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        db.Invitations.Add(invitation);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_supplier_invited", scope.UserId,
            referenceCode: rfq.ReferenceCode, changes: $"{{\"supplierId\":\"{command.SupplierId}\"}}", ct: ct);
        await db.SaveChangesAsync(ct);

        var recipientUserId = await db.Users.Where(u => u.SupplierId == command.SupplierId)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (recipientUserId is not null)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendRfqInvitationEmailAsync(recipientUserId.Value, rfq.Id, CancellationToken.None));
        }

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}
