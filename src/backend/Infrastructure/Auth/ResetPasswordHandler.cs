using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

/// <summary>
/// FR-IAM-005: on success, all existing sessions (refresh token families) are invalidated -
/// resetting a password must not leave old sessions alive.
/// </summary>
public sealed class ResetPasswordHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger) : IResetPasswordHandler
{
    public async Task<ResetPasswordResult> HandleAsync(ResetPasswordCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(command.UserId);
        if (user is null)
        {
            return new ResetPasswordResult.InvalidOrExpiredToken();
        }

        string decodedToken;
        try
        {
            decodedToken = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(command.Token));
        }
        catch (FormatException)
        {
            return new ResetPasswordResult.InvalidOrExpiredToken();
        }

        var result = await userManager.ResetPasswordAsync(user, decodedToken, command.NewPassword);
        if (!result.Succeeded)
        {
            var isTokenError = result.Errors.Any(e => e.Code is "InvalidToken");
            if (isTokenError)
            {
                return new ResetPasswordResult.InvalidOrExpiredToken();
            }

            return new ResetPasswordResult.WeakPassword([.. result.Errors.Select(e => e.Description)]);
        }

        var activeSessions = db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null);
        await foreach (var session in activeSessions.AsAsyncEnumerable().WithCancellation(ct))
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("User", user.Id, "password_reset", Guid.NewGuid(), user.Id, user.FullName, ct: ct);

        return new ResetPasswordResult.Success();
    }
}
