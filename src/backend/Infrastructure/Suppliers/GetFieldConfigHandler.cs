// Listing the administrator's switches over supplier fields.
//
// These replaced call sites and validator rules that had the answer written into them; the switch table's own
// header explains what the two families of switch decide.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetFieldConfigHandler(AppDbContext db) : IGetFieldConfigHandler
{
    public async Task<IReadOnlyList<FieldConfigDto>> HandleAsync(string? category, CancellationToken ct)
    {
        var query = db.Set<SupplierFieldConfig>().AsQueryable();
        if (category is not null) query = query.Where(c => c.Category == category);

        return await query
            .OrderBy(c => c.Category).ThenBy(c => c.FieldCode)
            .Select(c => new FieldConfigDto(c.Category, c.FieldCode, c.IsEnabled))
            .ToListAsync(ct);
    }
}
