// A supplier's own catalogue entries.
//
// An entry is only ever listed for the caller's own company, taken from the signed token and never from
// anything the caller sends. That is the row-scoping half of this feature's acceptance criteria, and the same
// pattern every other supplier-scoped list uses.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ListOfferingsHandler(AppDbContext db, IScopeContext scope) : IListOfferingsHandler
{
    public async Task<IReadOnlyList<OfferingDto>> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return [];

        var offerings = await db.Offerings
            .Where(o => o.SupplierId == scope.SupplierId)
            .OrderBy(o => o.NameEn)
            .ToListAsync(ct);

        return offerings.Select(OfferingDtoMapper.ToDto).ToList();
    }
}
