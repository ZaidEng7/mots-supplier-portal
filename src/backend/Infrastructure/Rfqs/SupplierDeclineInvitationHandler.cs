// A bidder declines an invitation, with a reason.
//
// Resolved through the same invitation-scoped loader every supplier-facing action uses, so a supplier who was
// not invited gets the same answer a wrong reference code would.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SupplierDeclineInvitationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : ISupplierDeclineInvitationHandler
{
    public async Task<SupplierRfqResult> HandleAsync(DeclineInvitationCommand command, CancellationToken ct)
    {
        var loaded = await SupplierRfqLoader.LoadInvitedAsync(db, scope, command.ReferenceCode, ct);
        if (loaded is null) return new SupplierRfqResult.NotFoundOrNotInvited();
        var (rfq, invitation) = loaded.Value;

        try
        {
            rfq.DeclineInvitation(scope.SupplierId!.Value, command.Reason);
        }
        catch (DomainException ex)
        {
            return new SupplierRfqResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_invitation_declined", scope.UserId,
            referenceCode: rfq.ReferenceCode, reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new SupplierRfqResult.Success(RfqDtoMapper.ToSupplierDto(rfq, invitation, scope.SupplierId!.Value));
    }
}
