// Signing out: ending, on the server, the session this browser's refresh cookie belongs to.
//
// Signing out used to delete the cookie and nothing else. The session stayed live on the server until its refresh
// token expired, up to thirty days later, so a copy of the cookie taken before the sign-out still refreshed, and
// the person's own session list and the staff screens still counted a session they had ended.
//
// The whole family is revoked, every token that sign-in rotated through, rather than only the token presented.
// The presented one may be a token rotated away a moment ago by a parallel request, and revoking only it would
// leave its successor alive.
//
// A missing or unknown cookie revokes nothing and records nothing, and is not an error: from the browser's side
// signing out always succeeds, and there is no session to name. A cookie that resolves names its owner, so the
// sign-out is recorded against that person, in the same save as the revocation.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class LogoutHandler(AppDbContext db, IAuditLogger auditLogger) : ILogoutHandler
{
    public async Task HandleAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        var hash = TokenHasher.Hash(refreshToken);
        var presented = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (presented is null)
        {
            return;
        }

        var family = await db.RefreshTokens
            .Where(t => t.FamilyId == presented.FamilyId && t.RevokedAt == null)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var t in family) t.RevokedAt = now;

        await auditLogger.LogAsync("User", presented.UserId, SessionAuditActions.Logout, presented.UserId, ct: ct);
        await db.SaveChangesAsync(ct);
    }
}
