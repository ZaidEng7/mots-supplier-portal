// Answers what the banner across the top of every screen should say, narrowed to what the caller is
// entitled to know.
//
// Four scopes, one question. A supplier sees only their own award's failure. Buying staff see failures
// inside their own organization, and nobody sees another organization's, which is the same boundary every
// other buyer-side read draws. The platform administrator sees the whole registry's, and anybody else is
// told nothing.
//
// THE PLATFORM ADMINISTRATOR'S SCOPE IS WIDE ON PURPOSE. They belong to no organisation, so neither of the
// narrower scopes reaches them, and without their own the banner stayed silent for the one person whose
// Operations page lists every award's send and who can retry it. A caller with neither a supplier nor an
// organisation who holds integration.retry is told when any award in the registry has failed, which is no
// more than that page already shows them. Having no organisation is not enough on its own: a reviewer or
// the ministry's viewer has none either, and is told nothing.
//
// Not configured is not the same as degraded, and it is not shown to everybody. It means purchase orders are
// not sent to the ERP in this environment: while the transport is the logging stand-in, an award's purchase
// order is logged and sent nowhere. It no longer means the portal has no ERP at all, because the supplier
// import and push talk to the ERP through a connection of their own, which this flag does not look at. A supplier told that the ministry sends no
// purchase orders has learned something about the ministry's deployment rather than about their own bid. It
// goes to the callers who could act on it.

namespace MotsSupplierPortal.Infrastructure.Platform;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Platform;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SystemStatusHandler(AppDbContext db, IScopeContext scope, IOutboxTransport transport)
    : ISystemStatusHandler
{
    public async Task<SystemStatusDto> HandleAsync(CancellationToken ct)
    {
        var degraded = false;

        if (scope.SupplierId is { } supplierId)
        {
            degraded = await db.Awards.AsNoTracking().AnyAsync(
                a => a.ErpSyncStatus == ErpSyncStatus.Failed
                     && db.Proposals.Any(p => p.Id == a.WinningProposalId && p.SupplierId == supplierId), ct);
        }
        else if (scope.OrganizationId is { } organizationId)
        {
            degraded = await db.Awards.AsNoTracking().AnyAsync(
                a => a.ErpSyncStatus == ErpSyncStatus.Failed
                     && db.Rfqs.Any(r => r.Id == a.RfqId && r.OrganizationId == organizationId), ct);
        }
        else if (scope.HasPermission(Permissions.IntegrationRetry))
        {
            degraded = await db.Awards.AsNoTracking().AnyAsync(a => a.ErpSyncStatus == ErpSyncStatus.Failed, ct);
        }

        var notConfigured = scope.HasPermission(Permissions.IntegrationRetry)
                            && transport is LoggingOutboxTransport;

        return new SystemStatusDto(degraded, notConfigured);
    }
}
