using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// One offering, scoped to the caller's own supplier IN THE QUERY - a miss is indistinguishable from
/// an id that never existed (§9.2), same as every other supplier-scoped read here.
/// </summary>
public sealed class GetOfferingHandler(AppDbContext db, IScopeContext scope) : IGetOfferingHandler
{
    public async Task<OfferingDto?> HandleAsync(Guid offeringId, CancellationToken ct)
    {
        if (scope.SupplierId is null) return null;

        var offering = await db.Offerings.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == offeringId && o.SupplierId == scope.SupplierId, ct);

        return offering is null ? null : OfferingDtoMapper.ToDto(offering);
    }
}
