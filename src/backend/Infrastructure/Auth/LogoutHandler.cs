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
// It runs under SessionLock, so a refresh of the same session either finishes first, and its successor is revoked
// here, or waits and finds the session already ended. Without the lock a refresh committing between this reading
// the family and revoking it left its successor live after a sign-out that was recorded as done. A token already
// revoked keeps the time it was revoked at, because that time is what marks a rotated token as rotated.
//
// A missing or unknown cookie revokes nothing and records nothing, and is not an error: from the browser's side
// signing out always succeeds, and there is no session to name. A cookie that resolves names its owner, and the
// sign-out is recorded against that person, in the same transaction as the revocation, only when it revoked
// something. A cookie whose session had already ended is a sign-out that ended nothing.

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
        var presented = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.TokenHash == hash)
            .Select(t => new { t.UserId, t.FamilyId })
            .FirstOrDefaultAsync(ct);

        if (presented is null)
        {
            return;
        }

        await using var transaction = await SessionLock.BeginAsync(db, presented.UserId, ct);

        var revoked = await SessionLock.RevokeAsync(db, t => t.FamilyId == presented.FamilyId, DateTimeOffset.UtcNow, ct);
        if (revoked > 0)
        {
            await auditLogger.LogAsync("User", presented.UserId, SessionAuditActions.Logout, presented.UserId, ct: ct);
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }
}
