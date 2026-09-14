// A bidder opens a tender they were invited to.
//
// Opening it marks the invitation as viewed, as a side effect of a successful read. That is the first time an
// invited supplier actually opens the tender rather than merely listing it, which is why the audit row and the
// save only happen on that first view.

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

public sealed class SupplierGetRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISupplierGetRfqHandler
{
    public async Task<SupplierRfqResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        var loaded = await SupplierRfqLoader.LoadInvitedAsync(db, scope, referenceCode, ct);
        if (loaded is null) return new SupplierRfqResult.NotFoundOrNotInvited();
        var (rfq, invitation) = loaded.Value;

        var wasFirstView = invitation.Status == InvitationStatus.Invited;
        rfq.MarkInvitationViewed(scope.SupplierId!.Value);
        if (wasFirstView)
        {
            await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_invitation_viewed", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
            await db.SaveChangesAsync(ct);
        }

        return new SupplierRfqResult.Success(RfqDtoMapper.ToSupplierDto(rfq, invitation, scope.SupplierId!.Value));
    }
}
