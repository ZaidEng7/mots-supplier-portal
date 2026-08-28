using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// FR-ONB-009 post-approval lifecycle (MSP-63). The transitions themselves live on the aggregate;
/// this handler owns persistence, auditing, and the part BRULE-008 requires that the domain cannot
/// reach: revoking the supplier's users' access on deactivation.
/// </summary>
public sealed class SupplierLifecycleHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IScopeContext scope,
    IAuditLogger auditLogger) : ISupplierLifecycleHandler
{
    public Task<SupplierLifecycleResult> SuspendAsync(SupplierLifecycleCommand command, CancellationToken ct) =>
        TransitionAsync(command, (s, reason) => s.Suspend(reason), "supplier_suspended", ct);

    public Task<SupplierLifecycleResult> ReactivateAsync(SupplierLifecycleCommand command, CancellationToken ct) =>
        TransitionAsync(command, (s, reason) => s.Reactivate(reason), "supplier_reactivated", ct);

    public Task<SupplierLifecycleResult> DeactivateAsync(SupplierLifecycleCommand command, CancellationToken ct) =>
        TransitionAsync(command, (s, reason) => s.Deactivate(reason), "supplier_deactivated", ct,
            revokeUserAccess: true);

    private async Task<SupplierLifecycleResult> TransitionAsync(
        SupplierLifecycleCommand command,
        Action<Domain.Suppliers.Supplier, string> transition,
        string auditAction,
        CancellationToken ct,
        bool revokeUserAccess = false)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.ReferenceCode == command.ReferenceCode, ct);
        if (supplier is null)
        {
            return new SupplierLifecycleResult.NotFound();
        }

        var stateBefore = supplier.LifecycleState;

        try
        {
            transition(supplier, command.Reason);
        }
        catch (DomainException ex)
        {
            // NFR-CMP-003 / BRULE-097: surfaced as a typed result carrying the domain's own message,
            // so the caller learns why the transition was refused rather than being told "no".
            return new SupplierLifecycleResult.Invalid(ex.Message);
        }

        if (revokeUserAccess)
        {
            await RevokeSupplierUsersAsync(supplier.Id, ct);
        }

        await auditLogger.LogAsync(
            "Supplier", supplier.Id, auditAction, scope.UserId,
            fromState: stateBefore.ToString(),
            toState: supplier.LifecycleState.ToString(),
            reason: command.Reason,
            referenceCode: supplier.ReferenceCode,
            ct: ct);

        await db.SaveChangesAsync(ct);

        return new SupplierLifecycleResult.Success(supplier.LifecycleState.ToString());
    }

    /// <summary>
    /// BRULE-008: a deactivated supplier's logins are revoked.
    ///
    /// Both halves are necessary and neither is sufficient. IsActive=false stops a NEW login
    /// (LoginHandler checks it) and stops a refresh (RefreshTokenHandler checks it too), but a
    /// refresh-token family left alive is a credential still sitting in a browser - so the families
    /// are killed as well, matching what DisableSupplierUserHandler already does for a single user.
    ///
    /// Setting the supplier's state alone would look identical in the database and leave every one
    /// of its users able to keep working. That is why the tests assert an actual failed login and
    /// an actual failed refresh rather than inspecting the state column.
    /// </summary>
    private async Task RevokeSupplierUsersAsync(Guid supplierId, CancellationToken ct)
    {
        var users = await userManager.Users.Where(u => u.SupplierId == supplierId).ToListAsync(ct);

        foreach (var user in users)
        {
            user.IsActive = false;
            await userManager.UpdateAsync(user);
        }

        var userIds = users.Select(u => u.Id).ToList();
        var liveSessions = db.RefreshTokens.Where(t => userIds.Contains(t.UserId) && t.RevokedAt == null);

        await foreach (var session in liveSessions.AsAsyncEnumerable().WithCancellation(ct))
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
        }
    }
}
