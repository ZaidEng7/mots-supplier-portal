// The active governorates, for the pickers that offer them.

namespace MotsSupplierPortal.Infrastructure.ReferenceData;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetRegionsHandler(AppDbContext db) : IGetRegionsHandler
{
    public async Task<IReadOnlyList<RegionDto>> HandleAsync(CancellationToken ct)
    {
        return await db.Regions
            .Where(r => r.IsActive)
            .OrderBy(r => r.Code)
            .Select(r => new RegionDto(r.Id, r.Code, r.NameAr, r.NameEn))
            .ToListAsync(ct);
    }
}
