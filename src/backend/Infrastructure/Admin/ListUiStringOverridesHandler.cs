using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Admin;

public sealed class ListUiStringOverridesHandler(AppDbContext db) : IListUiStringOverridesHandler
{
    public async Task<IReadOnlyList<UiStringOverrideDto>> HandleAsync(CancellationToken ct) =>
        await db.UiStringOverrides.AsNoTracking()
            .OrderBy(o => o.Key).ThenBy(o => o.Language)
            .Select(o => new UiStringOverrideDto(o.Key, o.Language, o.Value, o.UpdatedAt))
            .ToListAsync(ct);
}
