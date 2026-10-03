// Signing out one named session.
//
// Scoped to the caller's own sessions, so a family identifier belonging to somebody else simply does not match.
//
// It runs under SessionLock, so a refresh of that session either finishes first and its successor is revoked
// here, or waits and finds the session ended. Only tokens not yet revoked are revoked, so a rotated token keeps the
// time it was rotated at.
//
// The audit row is stored in the same transaction as the revocation. It used to be added after the save that
// revoked the session, and with nothing saving again it was dropped.

namespace MotsSupplierPortal.Infrastructure.Auth;

using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class RevokeSessionHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRevokeSessionHandler
{
    public async Task<bool> HandleAsync(Guid familyId, CancellationToken ct)
    {
        if (scope.UserId is not { } userId)
        {
            return false;
        }

        await using var transaction = await SessionLock.BeginAsync(db, userId, ct);

        var revoked = await SessionLock.RevokeAsync(
            db, t => t.UserId == userId && t.FamilyId == familyId, DateTimeOffset.UtcNow, ct);

        if (revoked == 0)
        {
            return false;
        }

        await auditLogger.LogAsync("User", userId, SessionAuditActions.SessionRevoked, userId, ct: ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
