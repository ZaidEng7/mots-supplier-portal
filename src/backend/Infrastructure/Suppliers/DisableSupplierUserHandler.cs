// A supplier administrator disables one of their colleagues' accounts.
//
// Disabling revokes access immediately: the account is marked inactive AND every live refresh-token family
// is ended, so a session already open cannot keep working.
//
// The same pattern a password change uses to end other sessions.
//
// The inactive flag, the ended sessions and the audit row are one transaction, opened by SessionLock before the
// account is written. The framework saves the flag through its own call, and that save used to commit on its own
// before the sessions were ended and the row stored. The lock also makes a refresh in flight either finish first, so
// its successor is ended here, or wait and find the session over. The logger only adds a row, and this row used to
// be added after the last save, so every disable answered success and left nothing in the trail.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class DisableSupplierUserHandler(AppDbContext db, UserManager<AppUser> userManager, IScopeContext scope, IAuditLogger auditLogger) : IDisableSupplierUserHandler
{
    public async Task<DisableSupplierUserResult> HandleAsync(DisableSupplierUserCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new DisableSupplierUserResult.NotFoundOrOutOfScope();

        var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == command.UserId && u.SupplierId == scope.SupplierId, ct);
        if (user is null) return new DisableSupplierUserResult.NotFoundOrOutOfScope();

        await using var transaction = await SessionLock.BeginAsync(db, user.Id, ct);

        user.IsActive = false;
        await userManager.UpdateAsync(user);

        await SessionLock.RevokeAsync(db, t => t.UserId == user.Id, DateTimeOffset.UtcNow, ct);
        await auditLogger.LogAsync("Supplier", scope.SupplierId.Value, "supplier_user_disabled", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new DisableSupplierUserResult.Success();
    }
}
