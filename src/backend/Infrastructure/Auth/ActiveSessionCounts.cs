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
//
// The administrator's dashboard asks the same question of every account at once, and gets two answers, one for
// staff and one for suppliers, never added together. Each answer is the number of sessions open and the number
// of people holding at least one. It is the same rule over the same rows, kept here so the dashboard's figure
// and the one beside each account on the staff list cannot count a session differently.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

internal sealed record ActiveSessionTotals(int Sessions, int People);

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

    public static async Task<(ActiveSessionTotals Staff, ActiveSessionTotals Suppliers)> ByAccountKindAsync(
        AppDbContext db, CancellationToken ct)
    {
        var totals = await db.RefreshTokens
            .Where(RefreshToken.Active)
            .Join(db.Users, t => t.UserId, u => u.Id, (t, u) => new { t.FamilyId, t.UserId, Supplier = u.SupplierId != null })
            .GroupBy(x => x.Supplier)
            .Select(g => new
            {
                Supplier = g.Key,
                Sessions = g.Select(x => x.FamilyId).Distinct().Count(),
                People = g.Select(x => x.UserId).Distinct().Count(),
            })
            .ToListAsync(ct);

        ActiveSessionTotals For(bool supplier) =>
            totals.Where(x => x.Supplier == supplier).Select(x => new ActiveSessionTotals(x.Sessions, x.People))
                .SingleOrDefault() ?? new ActiveSessionTotals(0, 0);

        return (For(supplier: false), For(supplier: true));
    }
}
