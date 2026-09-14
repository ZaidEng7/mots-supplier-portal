// A signed-in user changes their own password.
//
// The current password is verified first, so the refusal names the real reason. The framework would otherwise
// report it as a generic password failure alongside strength errors.
//
// The change itself goes through the framework's own change method rather than a hand-rolled verify-and-reset:
// it checks the current password against the same hasher that issued it, and re-stamps the security stamp, both
// of which a hand-rolled version gets wrong quietly.
//
//
// EVERY OTHER SESSION IS REVOKED, NOT THIS ONE
//
// A reset revokes everything, because the person holding the session may be the attacker. A deliberate change
// by a signed-in user is the opposite situation, and signing them out of the tab they just used would read as
// the change having failed.
//
// Which session is "this one" is resolved the same way the revoke-all handler does it, by hashing the presented
// token and finding its family, rather than a second way of answering the same question.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ChangePasswordHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger) : IChangePasswordHandler
{
    public async Task<ChangePasswordResult> HandleAsync(ChangePasswordCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null) return new ChangePasswordResult.UserNotFound();

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
