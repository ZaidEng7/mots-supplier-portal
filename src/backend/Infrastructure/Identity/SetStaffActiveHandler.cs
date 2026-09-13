using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>
/// T-077: deactivate or restore a staff account.
///
/// <para>Deactivation, never deletion - the same reasoning D-28 records for reference data, and more
/// strongly here: this account is the actor on audit rows, and an audit trail that points at a row
/// that no longer exists is not an audit trail. `IsActive = false` plus every session revoked is what
/// "removed" means for a person.</para>
/// </summary>
public sealed class SetStaffActiveHandler(
    AppDbContext db, UserManager<AppUser> userManager, IScopeContext scope, IAuditLogger auditLogger)
    : ISetStaffActiveHandler
{
    public async Task<StaffAccountResult> HandleAsync(Guid userId, bool isActive, CancellationToken ct)
    {
        var user = await StaffAccountLoader.LoadAsync(userManager, userId, ct);
        if (user is null) return new StaffAccountResult.NotFound();

        // Deactivating yourself is refused. Not paternalism: an administrator who does it is locked out
        // of the surface that would undo it, and if they were the last one nothing can.
        if (!isActive && user.Id == scope.UserId) return new StaffAccountResult.CannotActOnSelf();

        if (!isActive && await StaffAccountLoader.WouldLeaveNoAdministratorAsync(db, userManager, user, ct))
        {
            return new StaffAccountResult.WouldLockOutAdministration();
        }

        user.IsActive = isActive;
        await userManager.UpdateAsync(user);

        if (!isActive)
        {
            // Every live session dies with the account. Leaving them alive would make "deactivated" mean
            // "cannot sign in again", which is not what an administrator removing an account in error
            // needs it to mean.
            await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
        }

        await auditLogger.LogAsync("StaffAccount", user.Id,
            isActive ? "staff_reactivated" : "staff_deactivated", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);

        return new StaffAccountResult.Success(await StaffAccountLoader.ToDtoAsync(db, userManager, user, ct));
    }
}
