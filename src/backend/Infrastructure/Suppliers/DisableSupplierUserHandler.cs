using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.8/MSP-55 AC3: disabling immediately revokes access - IsActive=false AND every
/// active refresh-token family killed, so a live session can't keep working after disable (same
/// pattern as ResetPasswordHandler's session revocation on password change).</summary>
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
