// Reading one catalogue entry, scoped to the caller's own company inside the query.
//
// A miss is indistinguishable from an identifier that never existed, like every other supplier-scoped read
// here.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

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
