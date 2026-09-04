using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.9/FEAT-04.2: admin-editable config replacing what used to be hardcoded call
/// sites/validator rules - see SupplierFieldConfig's own doc comment.</summary>
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

/// <summary>T-029: the read that makes the guarded PUT usable at all.</summary>
public sealed class GetOneFieldConfigHandler(AppDbContext db) : IGetOneFieldConfigHandler
{
    public async Task<FieldConfigDetailDto?> HandleAsync(string category, string fieldCode, CancellationToken ct)
        => await db.Set<SupplierFieldConfig>()
            .Where(c => c.Category == category && c.FieldCode == fieldCode)
            .Select(c => new FieldConfigDetailDto(c.Category, c.FieldCode, c.IsEnabled, c.RowVersion))
            .FirstOrDefaultAsync(ct);
}

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
