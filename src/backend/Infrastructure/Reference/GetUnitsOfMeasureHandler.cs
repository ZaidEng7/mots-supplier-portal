using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Reference;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Reference;

public sealed class GetUnitsOfMeasureHandler(AppDbContext db) : IGetUnitsOfMeasureHandler
{
    public async Task<IReadOnlyList<UnitOfMeasureDto>> HandleAsync(CancellationToken ct)
    {
        return await db.UnitsOfMeasure
            .Where(u => u.IsActive)
            .OrderBy(u => u.Code)
            .Select(u => new UnitOfMeasureDto(u.Id, u.Code, u.NameAr, u.NameEn))
            .ToListAsync(ct);
    }
}
