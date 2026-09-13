using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>Marks the invitation Viewed as a side effect of a successful fetch (FEAT-08.6) - the
/// first time an invited supplier actually opens the RFQ, not merely lists it.</summary>
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
