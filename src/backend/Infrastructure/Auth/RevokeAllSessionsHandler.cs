using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

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

        foreach (var t in tokens) t.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var revokedFamilies = tokens.Select(t => t.FamilyId).Distinct().Count();
        await auditLogger.LogAsync("User", scope.UserId.Value, "sessions_revoked_all", scope.UserId, ct: ct);
        return revokedFamilies;
    }
}
