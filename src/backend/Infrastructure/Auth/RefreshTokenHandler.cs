// Exchanging a refresh token for a new pair, and detecting theft.
//
// The token rotates on use: the presented one is revoked and a new one issued in the same family.
//
// Presenting a token that was already rotated out, or revoked, or has expired, revokes the ENTIRE family and
// forces a fresh sign-in. That is the classic detection: the legitimate holder and the thief cannot both use
// one token, so a second use means one of them is not the owner and neither keeps the session.
//
// An account that has since been deactivated cannot refresh either, which is half of what makes deactivation
// immediate.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class RefreshTokenHandler(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IAuditLogger auditLogger,
    LoginHandler loginHandler) : IRefreshTokenHandler
{
    public async Task<RefreshTokenResult> HandleAsync(RefreshTokenCommand command, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(command.RefreshToken);
        var presented = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (presented is null)
        {
            return new RefreshTokenResult.Invalid();
        }

        if (presented.RevokedAt is not null || presented.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            var family = await db.RefreshTokens
                .Where(t => t.FamilyId == presented.FamilyId && t.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var t in family) t.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await auditLogger.LogAsync("User", presented.UserId, "refresh_reuse_detected", presented.UserId, ct: ct);
            return new RefreshTokenResult.ReuseDetected();
        }

        var user = await userManager.FindByIdAsync(presented.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            return new RefreshTokenResult.Invalid();
        }

        presented.RevokedAt = DateTimeOffset.UtcNow;
        var tokens = await loginHandler.IssueTokenPairAsync(user, presented.FamilyId, command.Ip, command.UserAgent, ct);
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("User", user.Id, "refresh_rotated", user.Id, user.FullName, ct: ct);

        return new RefreshTokenResult.Success(tokens);
    }
}
