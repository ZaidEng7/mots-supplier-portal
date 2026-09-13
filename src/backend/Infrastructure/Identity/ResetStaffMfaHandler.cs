// Clearing somebody's authenticator enrolment.
//
// A system administrator cannot hold a session without a second factor, so one who loses their authenticator is
// locked out with no self-service route. This is that route.
//
// It is deliberately an action on somebody ELSE's account. A reset available to the holder would be a way past
// the second factor, and resetting your own is refused for exactly that reason: the point of a second factor is
// that holding the first one is not enough, and a session is the first one.
//
// Every session is revoked. A reset that left them alive would hand an attacker who already holds one a way to
// stay past the very control being reset.

namespace MotsSupplierPortal.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ResetStaffMfaHandler(
    AppDbContext db, UserManager<AppUser> userManager, IScopeContext scope, IAuditLogger auditLogger)
    : IResetStaffMfaHandler
{
    public async Task<StaffAccountResult> HandleAsync(Guid userId, CancellationToken ct)
    {
        var user = await StaffAccountLoader.LoadAsync(userManager, userId, ct);
        if (user is null) return new StaffAccountResult.NotFound();

        if (user.Id == scope.UserId) return new StaffAccountResult.CannotActOnSelf();

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);

        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

        await auditLogger.LogAsync("StaffAccount", user.Id, "staff_mfa_reset", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);

        return new StaffAccountResult.Success(await StaffAccountLoader.ToDtoAsync(db, userManager, user, ct));
    }
}
