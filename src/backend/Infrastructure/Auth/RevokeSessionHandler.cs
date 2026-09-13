using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

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
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("User", scope.UserId.Value, "session_revoked", scope.UserId, ct: ct);
        return true;
    }
}
