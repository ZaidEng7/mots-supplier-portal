using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>
/// T-077/SCR-702: clear an authenticator enrolment.
///
/// <para>`system_admin` cannot hold a session without MFA, so an administrator who loses their
/// authenticator is locked out with no self-service path. This is that path, and it is deliberately an
/// administrator action on someone ELSE's account: a reset available to the holder would be a way past
/// the second factor.</para>
/// </summary>
public sealed class ResetStaffMfaHandler(
    AppDbContext db, UserManager<AppUser> userManager, IScopeContext scope, IAuditLogger auditLogger)
    : IResetStaffMfaHandler
{
    public async Task<StaffAccountResult> HandleAsync(Guid userId, CancellationToken ct)
    {
        var user = await StaffAccountLoader.LoadAsync(userManager, userId, ct);
        if (user is null) return new StaffAccountResult.NotFound();

        // Resetting your own MFA is refused: the point of the second factor is that possessing the first
        // one is not enough, and a session is the first one.
        if (user.Id == scope.UserId) return new StaffAccountResult.CannotActOnSelf();

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);

        // Every session revoked. A reset that left them alive would hand an attacker who already holds
        // one a way to stay past the very control being reset.
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

        await auditLogger.LogAsync("StaffAccount", user.Id, "staff_mfa_reset", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);

        return new StaffAccountResult.Success(await StaffAccountLoader.ToDtoAsync(db, userManager, user, ct));
    }
}
