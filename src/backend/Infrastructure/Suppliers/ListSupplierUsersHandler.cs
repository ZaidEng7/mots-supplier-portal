using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.8/MSP-55: row-scoped to the caller's own SupplierId (STORY-01.8.1).</summary>
public sealed class ListSupplierUsersHandler(AppDbContext db, IScopeContext scope) : IListSupplierUsersHandler
{
    public async Task<IReadOnlyList<SupplierUserDto>> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return [];

        return await db.Users
            .Where(u => u.SupplierId == scope.SupplierId)
            .OrderBy(u => u.Email)
            .Select(u => new SupplierUserDto(u.Id, u.Email!, u.FullName, u.IsActive))
            .ToListAsync(ct);
    }
}
