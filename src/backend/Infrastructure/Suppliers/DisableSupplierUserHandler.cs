// A supplier administrator disables one of their colleagues' accounts.
//
// Disabling revokes access immediately: the account is marked inactive AND every live refresh-token family
// is ended, so a session already open cannot keep working.
//
// The same pattern a password change uses to end other sessions.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class DisableSupplierUserHandler(AppDbContext db, UserManager<AppUser> userManager, IScopeContext scope, IAuditLogger auditLogger) : IDisableSupplierUserHandler
{
    public async Task<DisableSupplierUserResult> HandleAsync(DisableSupplierUserCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new DisableSupplierUserResult.NotFoundOrOutOfScope();

        var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && u.SupplierId == scope.SupplierId, ct);
        if (user is null) return new DisableSupplierUserResult.NotFoundOrOutOfScope();

        user.IsActive = false;
        await userManager.UpdateAsync(user);

        var activeSessions = db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null);
        await foreach (var session in activeSessions.AsAsyncEnumerable().WithCancellation(ct))
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("Supplier", scope.SupplierId.Value, "supplier_user_disabled", scope.UserId, ct: ct);

        return new DisableSupplierUserResult.Success();
    }
}
