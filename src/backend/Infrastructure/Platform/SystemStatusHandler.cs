using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Platform;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Platform;

/// <summary>
/// SCR-045. Three scopes, one question, and the narrowest answer each caller is entitled to.
///
/// <para>A supplier sees only their own award's failure. Buyer staff see failures inside their own
/// organization. Nobody sees another organization's, which is the same boundary BRULE-029 draws for
/// every other buyer-side read.</para>
///
/// <para><b>"Not configured" is not the same as "degraded" and is not shown to everyone.</b> A
/// supplier told that the ministry has no ERP integration has learned something about the ministry's
/// deployment, not about their own bid. It goes to callers holding <c>integration.retry</c> — the
/// people who would act on it.</para>
/// </summary>
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

        // Same check SCR-700's tile makes, kept in one shape rather than two: a transport that is the
        // logging stand-in is not an integration (T-089).
        var notConfigured = scope.HasPermission(Permissions.IntegrationRetry)
                            && transport is LoggingOutboxTransport;

        return new SystemStatusDto(degraded, notConfigured);
    }
}
