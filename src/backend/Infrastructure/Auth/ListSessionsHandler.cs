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

        if (KeysetCursor.TryDecode(cursor, out var from))
        {
            ordered = ordered.Where(s =>
                s.CreatedAt < from.At
                || (s.CreatedAt == from.At && s.FamilyId.CompareTo(from.Id) < 0));
        }

        var page = ordered.Take(pageSize + 1).ToList();
        var hasMore = page.Count > pageSize;
        var items = hasMore ? page[..pageSize] : page;

        return ListEnvelope<SessionDto>.Cursor(
            items,
            hasMore,
            hasMore ? new KeysetCursor(items[^1].CreatedAt, items[^1].FamilyId).Encode() : null,
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
