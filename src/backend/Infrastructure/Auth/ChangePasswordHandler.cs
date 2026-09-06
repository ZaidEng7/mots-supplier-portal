using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

/// <summary>
/// SCR-903. Verifies the current password through Identity, then changes it and revokes every other
/// session.
///
/// <para><b>Other sessions, not this one.</b> A reset revokes everything because the person holding
/// the session may be the attacker; a deliberate change by a signed-in user is the opposite
/// situation, and signing them out of the tab they just used would read as the change having failed.
/// Every OTHER refresh-token family is revoked, which is what a password change is for.</para>
///
/// <para>Reuses <see cref="UserManager{T}.ChangePasswordAsync"/> rather than verifying and resetting
/// by hand: it checks the current password against the same hasher that issued it, and re-stamps the
/// security stamp, both of which a hand-rolled version gets wrong quietly.</para>
/// </summary>
public sealed class ChangePasswordHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger) : IChangePasswordHandler
{
    public async Task<ChangePasswordResult> HandleAsync(ChangePasswordCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null) return new ChangePasswordResult.UserNotFound();

        // Checked before the change is attempted, so the refusal names the real reason. Identity
        // would otherwise report this as a generic password failure alongside strength errors.
        if (!await userManager.CheckPasswordAsync(user, command.CurrentPassword))
        {
            await auditLogger.LogAsync("User", user.Id, "password_change_refused", user.Id, user.FullName,
                reason: "incorrect current password", ct: ct);
            await db.SaveChangesAsync(ct);
            return new ChangePasswordResult.IncorrectCurrentPassword();
        }

        if (command.CurrentPassword == command.NewPassword)
        {
            return new ChangePasswordResult.SameAsCurrent();
        }

        var result = await userManager.ChangePasswordAsync(user, command.CurrentPassword, command.NewPassword);
        if (!result.Succeeded)
        {
            return new ChangePasswordResult.WeakPassword([.. result.Errors.Select(e => e.Description)]);
        }

        // Every OTHER session; the caller's own survives. Same resolution RevokeAllSessionsHandler
        // uses - hash the presented cookie, find its family, exclude that family - rather than a
        // second way of answering "which session is this one".
        Guid? currentFamilyId = null;
        if (!string.IsNullOrEmpty(command.CurrentRefreshToken))
        {
            var hash = TokenHasher.Hash(command.CurrentRefreshToken);
            currentFamilyId = (await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct))?.FamilyId;
        }

        var others = await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null
                        && (currentFamilyId == null || t.FamilyId != currentFamilyId))
            .ToListAsync(ct);
        foreach (var session in others) session.RevokedAt = DateTimeOffset.UtcNow;

        await auditLogger.LogAsync("User", user.Id, "password_changed", user.Id, user.FullName, ct: ct);
        await db.SaveChangesAsync(ct);

        return new ChangePasswordResult.Success();
    }
}
