// How many sessions each person has open, counted the way their own session list shows them.
//
// A session is one sign-in, and every token it rotates through shares that sign-in's FamilyId. So sessions are
// counted by family, never by token row. Counting the unrevoked rows instead counted every sign-in that had long
// since expired, because a token nobody presents again is never revoked, only left to expire. It would also count
// a family twice whenever it held two live tokens at once.
//
// A family counts while it still holds a token that RefreshToken.Active says is alive. That is the rule the
// session list filters on, so the number beside an account is the number of rows its holder would see there.
//
// One query answers for a whole page of accounts rather than one per row. An account with no live session is
// absent from the answer rather than present with zero.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class ActiveSessionCounts
{
    public static async Task<Dictionary<Guid, int>> ByUserAsync(
        AppDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        Guid[] ids = [.. userIds];
        return await db.RefreshTokens
            .Where(RefreshToken.Active)
            .Where(t => ids.Contains(t.UserId))
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, Sessions = g.Select(t => t.FamilyId).Distinct().Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Sessions, ct);
    }
}
