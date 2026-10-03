// Setting a new password from a reset link.
//
// The opaque single-use token from the link is the only lookup key. Once it resolves a user, a fresh framework
// reset token is generated and consumed internally to perform the actual change.
//
// Every existing session is invalidated on success. Resetting a password must not leave old sessions alive,
// because the person holding one may be who the reset is protecting against.
//
// The new password, the revoked sessions and the audit row are one transaction, opened by SessionLock before the
// password is written. The framework saves the password through its own call, and that save used to commit on its
// own before the revocation and the row, so a failure between them left a changed password with every old session
// still live and nothing recorded. The lock also makes a refresh in flight either finish first, so its successor is
// revoked here, or wait and find its session ended. The audit row used to be added after the last save, and with
// nothing saving again it was dropped.
//
// The link is consumed before that transaction and stays consumed whatever happens after it, as it always has.
//
// A token error from the framework is reported as an invalid link rather than as a weak password, so the caller
// is told which of the two actually happened.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ResetPasswordHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ISecurityTokenService securityTokenService,
    IAuditLogger auditLogger) : IResetPasswordHandler
{
    public async Task<ResetPasswordResult> HandleAsync(ResetPasswordCommand command, CancellationToken ct)
    {
        var consumed = await securityTokenService.ConsumeAsync(command.Token, SecurityTokenPurpose.PasswordReset, ct);
        if (consumed is not ConsumeSecurityTokenResult.Success success)
        {
            return new ResetPasswordResult.InvalidOrExpiredToken();
        }

        var user = await userManager.FindByIdAsync(success.UserId.ToString());
        if (user is null)
        {
            return new ResetPasswordResult.InvalidOrExpiredToken();
        }

        await using var transaction = await SessionLock.BeginAsync(db, user.Id, ct);

        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, identityToken, command.NewPassword);
        if (!result.Succeeded)
        {
            var isTokenError = result.Errors.Any(e => e.Code is "InvalidToken");
            if (isTokenError)
            {
                return new ResetPasswordResult.InvalidOrExpiredToken();
            }

            return new ResetPasswordResult.WeakPassword([.. result.Errors.Select(e => e.Description)]);
        }

        await SessionLock.RevokeAsync(db, t => t.UserId == user.Id, DateTimeOffset.UtcNow, ct);
        await auditLogger.LogAsync("User", user.Id, "password_reset", user.Id, user.FullName, ct: ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ResetPasswordResult.Success();
    }
}
