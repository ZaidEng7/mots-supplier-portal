using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

public sealed class ListSessionsHandler(AppDbContext db, IScopeContext scope) : IListSessionsHandler
{
    public async Task<IReadOnlyList<SessionDto>> HandleAsync(string? currentRefreshToken, CancellationToken ct)
    {
        if (scope.UserId is null)
        {
            return [];
        }

        var currentFamilyId = await ResolveCurrentFamilyIdAsync(currentRefreshToken, ct);

        var sessions = await db.RefreshTokens
            .Where(t => t.UserId == scope.UserId && t.RevokedAt == null && t.ExpiresAt > DateTimeOffset.UtcNow)
            .GroupBy(t => t.FamilyId)
            .Select(g => g.OrderByDescending(t => t.CreatedAt).First())
            .ToListAsync(ct);

        return [.. sessions
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new SessionDto(t.FamilyId, t.Ip, t.UserAgent, t.CreatedAt, t.ExpiresAt, t.FamilyId == currentFamilyId))];
    }

    private async Task<Guid?> ResolveCurrentFamilyIdAsync(string? currentRefreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(currentRefreshToken))
        {
            return null;
        }

        var hash = TokenHasher.Hash(currentRefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        return token?.FamilyId;
    }
}

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
