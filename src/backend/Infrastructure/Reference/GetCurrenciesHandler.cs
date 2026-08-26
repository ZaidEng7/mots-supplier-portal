using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Reference;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Reference;

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
