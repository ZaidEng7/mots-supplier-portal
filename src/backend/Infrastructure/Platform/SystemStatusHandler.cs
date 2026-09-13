// Answers what the banner across the top of every screen should say, narrowed to what the caller is
// entitled to know.
//
// Three scopes, one question. A supplier sees only their own award's failure. Buying staff see failures
// inside their own organization. Nobody sees another organization's, which is the same boundary every
// other buyer-side read draws.
//
// Not configured is not the same as degraded, and it is not shown to everybody. A supplier told that the
// ministry has no finance-system integration has learned something about the ministry's deployment rather
// than about their own bid. It goes to the callers who could act on it.

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

        var notConfigured = scope.HasPermission(Permissions.IntegrationRetry)
                            && transport is LoggingOutboxTransport;

        return new SystemStatusDto(degraded, notConfigured);
    }
}
