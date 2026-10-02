// Signing out one named session.
//
// Scoped to the caller's own sessions, so a family identifier belonging to somebody else simply does not match.
//
// The audit row is added before the save that revokes the session, so the two are one write. It used to be added
// after that save, and with nothing saving again it was dropped.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class RevokeSessionHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRevokeSessionHandler
{
    public async Task<bool> HandleAsync(Guid familyId, CancellationToken ct)
    {
        if (scope.UserId is null)
        {
            return false;
        }

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == scope.UserId && t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);

        if (tokens.Count == 0)
        {
            return false;
        }

        foreach (var t in tokens) t.RevokedAt = DateTimeOffset.UtcNow;

        await auditLogger.LogAsync("User", scope.UserId.Value, SessionAuditActions.SessionRevoked, scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
