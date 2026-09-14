// The active currencies, for the pickers that offer them.

namespace MotsSupplierPortal.Infrastructure.ReferenceData;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetCurrenciesHandler(AppDbContext db) : IGetCurrenciesHandler
{
    public async Task<IReadOnlyList<CurrencyDto>> HandleAsync(CancellationToken ct)
    {
        return await db.Currencies
            .Where(c => c.IsActive)
            .OrderBy(c => c.Code)
            .Select(c => new CurrencyDto(c.Id, c.Code, c.NameAr, c.NameEn))
            .ToListAsync(ct);
    }
}
