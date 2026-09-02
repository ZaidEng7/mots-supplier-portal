using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Auth;

public sealed class ListSessionsHandler(AppDbContext db, IScopeContext scope) : IListSessionsHandler
{
    public async Task<ListEnvelope<SessionDto>> HandleAsync(string? currentRefreshToken, string? cursor, int? limit, bool withCount, CancellationToken ct)
    {
        if (scope.UserId is null)
        {
            return ListEnvelope<SessionDto>.Empty(ListEnvelope<SessionDto>.DefaultPageSize);
        }

        var pageSize = ListEnvelope<SessionDto>.ClampPageSize(limit);
        var currentFamilyId = await ResolveCurrentFamilyIdAsync(currentRefreshToken, ct);

        // One row per session family, already reduced to a small, per-user-bounded set (a person
        // has a handful of active sessions, not thousands) - the keyset filter below runs against
        // this already-materialized list rather than pushing into SQL, since the boundary here is
        // the GroupBy-then-First reduction, not the row count.
        var sessions = await db.RefreshTokens
            .Where(t => t.UserId == scope.UserId && t.RevokedAt == null && t.ExpiresAt > DateTimeOffset.UtcNow)
            .GroupBy(t => t.FamilyId)
            .Select(g => g.OrderByDescending(t => t.CreatedAt).First())
            .ToListAsync(ct);

        var ordered = sessions
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.FamilyId)
            .Select(t => new SessionDto(t.FamilyId, t.Ip, t.UserAgent, t.CreatedAt, t.ExpiresAt, t.FamilyId == currentFamilyId))
            .AsEnumerable();

        // §6.1: "totalCount omitted unless ?withCount=true". Counted over the ordered set before
        // the cursor narrows it, so it is a total rather than "how many are left".
        int? totalCount = withCount ? ordered.Count() : null;

        if (SessionCursor.TryDecode(cursor, out var from))
        {
            ordered = ordered.Where(s =>
                s.CreatedAt < from.CreatedAt
                || (s.CreatedAt == from.CreatedAt && s.FamilyId.CompareTo(from.FamilyId) < 0));
        }

        var page = ordered.Take(pageSize + 1).ToList();
        var hasMore = page.Count > pageSize;
        var items = hasMore ? page[..pageSize] : page;

        return ListEnvelope<SessionDto>.Cursor(
            items,
            hasMore,
            hasMore ? new SessionCursor(items[^1].CreatedAt, items[^1].FamilyId).Encode() : null,
            pageSize,
            totalCount,
            sort: "-createdAt");
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
