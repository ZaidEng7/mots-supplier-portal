using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-06.1/FR-OFF-001: an offering is only ever listed for the caller's own supplier
/// (IScopeContext.SupplierId, derived from the JWT - never client input) - this is the row-scoping
/// half of FEAT-06.1's acceptance criteria, same pattern as every other supplier-scoped list.</summary>
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
