// The list of ministry staff accounts, which did not exist.
//
// Staff are the accounts with no company attached, which is the same test the staff invitation draws from the
// other side.
//
// A supplier's own users are administered by that supplier, and mixing the two would put a supplier's team in
// the platform administrator's list.
//
// The cursor type is the supplier-user one, reused rather than copied. The ordering is the same, and a second
// identical type would be a second thing to keep in step.
//
// The total is counted over the filtered set before the cursor narrows it, and only when asked for. Roles come
// from the identity framework's own join table in one query for the page rather than one per row.

namespace MotsSupplierPortal.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ListStaffHandler(AppDbContext db) : IListStaffHandler
{
    public async Task<ListEnvelope<StaffAccountDto>> HandleAsync(string? cursor, int? limit, bool withCount, CancellationToken ct)
    {
        var pageSize = ListEnvelope<StaffAccountDto>.ClampPageSize(limit);
        var query = db.Users.Where(u => u.SupplierId == null);

        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (SupplierUserCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(u =>
                u.Email!.CompareTo(from.Email) > 0
                || (u.Email == from.Email && u.Id.CompareTo(from.Id) > 0));
        }

        var rows = await query
            .OrderBy(u => u.Email).ThenBy(u => u.Id)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                u.IsActive,
                u.TwoFactorEnabled,
                u.LockoutEnd,
                ActiveSessions = db.RefreshTokens.Count(t => t.UserId == u.Id && t.RevokedAt == null),
            })
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        var pageIds = items.Select(r => r.Id).ToList();
        var roleByUser = await db.Set<IdentityUserRole<Guid>>()
            .Where(ur => pageIds.Contains(ur.UserId))
            .Join(db.Set<IdentityRole<Guid>>(), ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct);
        var roles = roleByUser
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.First().Name);

        return ListEnvelope<StaffAccountDto>.Cursor(
            [.. items.Select(r => new StaffAccountDto(
                r.Id, r.Email!, r.FullName, roles.GetValueOrDefault(r.Id), r.IsActive,
                r.TwoFactorEnabled, r.LockoutEnd, r.ActiveSessions))],
            hasMore,
            hasMore ? new SupplierUserCursor(items[^1].Email!, items[^1].Id).Encode() : null,
            pageSize,
            totalCount,
            sort: "email");
    }
}
