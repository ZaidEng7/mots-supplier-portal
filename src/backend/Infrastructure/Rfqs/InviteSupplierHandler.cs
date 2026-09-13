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

/// <summary>FEAT-08.1/FR-INV-001/BRULE-032: invite a candidate supplier. Active-only is enforced
/// here (cross-aggregate - see Rfq.InviteSupplier's own doc comment), not on the domain method.
/// FEAT-08.3/FR-INV-003: on success, enqueues a real email (not Outbox - see EmailJobs.cs's own
/// doc comment on why token/notification emails use the Hangfire+IEmailSender path, not the
/// ERP-integration Outbox) to the invited supplier's primary user. "In-app" is the invited
/// supplier's own RFQ list reflecting the new invitation on next fetch - the same shape "in-app"
/// has in every other transition in this codebase (no dedicated Notification entity exists
/// anywhere yet; EPIC-15 is unbuilt), not a gap invented for this feature alone.</summary>
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
