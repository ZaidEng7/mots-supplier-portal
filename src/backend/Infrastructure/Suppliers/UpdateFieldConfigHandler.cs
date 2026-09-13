using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class UpdateFieldConfigHandler(AppDbContext db) : IUpdateFieldConfigHandler
{
    public async Task<UpdateFieldConfigResult> HandleAsync(string category, string fieldCode, bool isEnabled, CancellationToken ct)
    {
        var config = await db.Set<SupplierFieldConfig>()
            .FirstOrDefaultAsync(c => c.Category == category && c.FieldCode == fieldCode, ct);
        if (config is null) return new UpdateFieldConfigResult.NotFound();

        config.IsEnabled = isEnabled;
        await db.SaveChangesAsync(ct);
        return new UpdateFieldConfigResult.Success(
            new FieldConfigDetailDto(config.Category, config.FieldCode, config.IsEnabled, config.RowVersion));
    }
}
