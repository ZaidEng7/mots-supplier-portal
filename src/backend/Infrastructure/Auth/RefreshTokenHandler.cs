using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

/// <summary>
/// STORY-01.1.1 AC3/AC4: refresh rotates on use. A reused (already-rotated-out or revoked) token
/// invalidates the entire family and forces re-login - classic refresh-token theft detection.
/// </summary>
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
            // Reuse of a rotated-out or expired token: revoke the whole family.
            var family = await db.RefreshTokens
                .Where(t => t.FamilyId == presented.FamilyId && t.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var t in family) t.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await auditLogger.LogAsync("User", presented.UserId, "refresh_reuse_detected", Guid.NewGuid(), presented.UserId, ct: ct);
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

        await auditLogger.LogAsync("User", user.Id, "refresh_rotated", Guid.NewGuid(), user.Id, user.FullName, ct: ct);

        return new RefreshTokenResult.Success(tokens);
    }
}
