// Signing out of every session, optionally keeping the one making the request.
//
// The current session is identified by hashing the presented token and reading its family, which is the same
// resolution the password change uses.
//
// The count returned is of session families rather than of token rows, because a family is what a person
// recognises as a session. Only families that were still live are counted, by RefreshToken's own rule, so the
// figure matches the sessions the person's list showed before. Every unrevoked token is still revoked, expired
// ones included, but a sign-in that had already expired was not a session this ended.
//
// The tokens are read and revoked under SessionLock, so a refresh in flight either finishes first and its successor
// is among the tokens read, or waits and finds its token revoked. Exactly the tokens read are revoked, so the count
// describes what this did, and none of them has an earlier revocation overwritten.
//
// The audit row is stored in the same transaction as the revocation. It used to be added after the save that
// revoked the sessions, and with nothing saving again it was dropped.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class RevokeAllSessionsHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRevokeAllSessionsHandler
{
    public async Task<int> HandleAsync(string? currentRefreshToken, bool excludeCurrent, CancellationToken ct)
    {
        if (scope.UserId is not { } userId)
        {
            return 0;
        }

        Guid? currentFamilyId = null;
        if (excludeCurrent && !string.IsNullOrEmpty(currentRefreshToken))
        {
            var hash = TokenHasher.Hash(currentRefreshToken);
            currentFamilyId = await db.RefreshTokens.AsNoTracking()
                .Where(t => t.TokenHash == hash)
                .Select(t => (Guid?)t.FamilyId)
                .FirstOrDefaultAsync(ct);
        }

        await using var transaction = await SessionLock.BeginAsync(db, userId, ct);

        var tokens = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && (currentFamilyId == null || t.FamilyId != currentFamilyId))
            .ToListAsync(ct);

        var revokedFamilies = tokens.Where(t => t.IsActive).Select(t => t.FamilyId).Distinct().Count();
        var ids = tokens.Select(t => t.Id).ToList();

        await SessionLock.RevokeAsync(db, t => ids.Contains(t.Id), DateTimeOffset.UtcNow, ct);
        await auditLogger.LogAsync("User", userId, SessionAuditActions.SessionsRevokedAll, userId, ct: ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return revokedFamilies;
    }
}
