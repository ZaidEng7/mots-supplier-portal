// The people who can sign in on behalf of one supplier.
//
// Scoped to the caller's own company.
//
// Ordered by email address, which is what this list is read by, with the identifier as tiebreak so a page
// boundary cannot repeat or drop a row.
//
// The total is a second query and is off unless asked for, and it is counted before the cursor narrows
// anything: a count of rows after the cursor is not a total, and would shrink as the caller pages.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ListSupplierUsersHandler(AppDbContext db, IScopeContext scope) : IListSupplierUsersHandler
{
    public async Task<ListEnvelope<SupplierUserDto>> HandleAsync(string? cursor, int? limit, bool withCount, CancellationToken ct)
    {
        if (scope.SupplierId is null) return ListEnvelope<SupplierUserDto>.Empty(ListEnvelope<SupplierUserDto>.DefaultPageSize);

        var pageSize = ListEnvelope<SupplierUserDto>.ClampPageSize(limit);
        var query = db.Users.Where(u => u.SupplierId == scope.SupplierId);

        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (SupplierUserCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(u =>
                u.Email!.CompareTo(from.Email) > 0
                || (u.Email == from.Email && u.Id.CompareTo(from.Id) > 0));
        }

        var rows = await query
            .OrderBy(u => u.Email).ThenBy(u => u.Id)
            .Select(u => new { u.Id, u.Email, Dto = new SupplierUserDto(u.Id, u.Email!, u.FullName, u.IsActive) })
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        return ListEnvelope<SupplierUserDto>.Cursor(
            [.. items.Select(r => r.Dto)],
            hasMore,
            hasMore ? new SupplierUserCursor(items[^1].Email!, items[^1].Id).Encode() : null,
            pageSize,
            totalCount,
            sort: "email");
    }
}
