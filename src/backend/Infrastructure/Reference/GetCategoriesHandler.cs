using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Reference;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Reference;

public sealed class GetCategoriesHandler(AppDbContext db) : IGetCategoriesHandler
{
    public async Task<IReadOnlyList<CategoryDto>> HandleAsync(CancellationToken ct)
    {
        return await db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Code)
            .Select(c => new CategoryDto(c.Id, c.Code, c.NameAr, c.NameEn))
            .ToListAsync(ct);
    }
}
