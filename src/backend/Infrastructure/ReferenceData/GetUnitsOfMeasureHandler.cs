// The active units of measure, for the pickers that offer them.

namespace MotsSupplierPortal.Infrastructure.ReferenceData;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

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
