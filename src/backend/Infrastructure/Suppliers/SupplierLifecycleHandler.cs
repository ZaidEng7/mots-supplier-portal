// Suspending, reactivating and deactivating an approved supplier.
//
// The transitions themselves live on the supplier. This handler owns saving them, recording them, and the
// part the rule requires that the domain cannot reach: taking away the supplier's users' access when the
// company is deactivated.
//
// A refused transition comes back as a typed result carrying the domain's own message, so the caller learns
// why rather than being told no.
//
//
// REVOKING ACCESS NEEDS BOTH HALVES
//
// Neither is sufficient on its own. Marking the accounts inactive stops a new sign-in and stops a refresh,
// because both paths check it. But a live refresh-token family is a credential still sitting in a browser,
// so the families are ended as well, matching what disabling a single user already does.
//
// Setting the supplier's state alone would look identical in the database and leave every one of its users
// able to keep working. That is why the tests assert an actual failed sign-in and an actual failed refresh
// rather than inspecting the state column.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

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
