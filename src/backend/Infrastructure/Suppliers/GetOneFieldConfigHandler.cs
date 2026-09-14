// Reading one administrator switch, which is what makes the guarded write usable at all.
//
// A caller cannot send an expected version it was never given, so the write's precondition needs a read that
// issues one.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetOneFieldConfigHandler(AppDbContext db) : IGetOneFieldConfigHandler
{
    public async Task<FieldConfigDetailDto?> HandleAsync(string category, string fieldCode, CancellationToken ct)
        => await db.Set<SupplierFieldConfig>()
            .Where(c => c.Category == category && c.FieldCode == fieldCode)
            .Select(c => new FieldConfigDetailDto(c.Category, c.FieldCode, c.IsEnabled, c.RowVersion))
            .FirstOrDefaultAsync(ct);
}
