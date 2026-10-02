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
// The audit row is added before the save that revokes the sessions, so the two are one write. It used to be added
// after that save, and with nothing saving again it was dropped.

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
        if (scope.UserId is null)
        {
            return 0;
        }

        Guid? currentFamilyId = null;
        if (excludeCurrent && !string.IsNullOrEmpty(currentRefreshToken))
        {
            var hash = TokenHasher.Hash(currentRefreshToken);
            var current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            currentFamilyId = current?.FamilyId;
        }

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == scope.UserId && t.RevokedAt == null && (currentFamilyId == null || t.FamilyId != currentFamilyId))
            .ToListAsync(ct);

        var revokedFamilies = tokens.Where(t => t.IsActive).Select(t => t.FamilyId).Distinct().Count();

        foreach (var t in tokens) t.RevokedAt = DateTimeOffset.UtcNow;

        await auditLogger.LogAsync("User", scope.UserId.Value, SessionAuditActions.SessionsRevokedAll, scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);
        return revokedFamilies;
    }
}
