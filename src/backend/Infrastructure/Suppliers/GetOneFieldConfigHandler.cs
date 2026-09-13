using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>T-029: the read that makes the guarded PUT usable at all.</summary>
public sealed class GetOneFieldConfigHandler(AppDbContext db) : IGetOneFieldConfigHandler
{
    public async Task<FieldConfigDetailDto?> HandleAsync(string category, string fieldCode, CancellationToken ct)
        => await db.Set<SupplierFieldConfig>()
            .Where(c => c.Category == category && c.FieldCode == fieldCode)
            .Select(c => new FieldConfigDetailDto(c.Category, c.FieldCode, c.IsEnabled, c.RowVersion))
            .FirstOrDefaultAsync(ct);
}
