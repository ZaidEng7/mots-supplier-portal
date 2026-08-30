using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.8/MSP-55: row-scoped to the caller's own SupplierId (STORY-01.8.1).</summary>
public sealed class ListSupplierUsersHandler(AppDbContext db, IScopeContext scope) : IListSupplierUsersHandler
{
    public async Task<Page<SupplierUserDto>> HandleAsync(string? cursor, int? limit, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new Page<SupplierUserDto>([], false);

        var pageSize = Page<SupplierUserDto>.ClampLimit(limit);
        var query = db.Users.Where(u => u.SupplierId == scope.SupplierId);

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

        return new Page<SupplierUserDto>(
            [.. items.Select(r => r.Dto)],
            hasMore,
            hasMore ? new SupplierUserCursor(items[^1].Email!, items[^1].Id).Encode() : null);
    }
}
