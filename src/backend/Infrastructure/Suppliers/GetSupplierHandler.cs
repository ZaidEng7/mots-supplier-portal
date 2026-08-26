using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// Scoping is applied IN the query, not post-filtered (STORY-01.8.1 AC4). A supplier user
/// requesting a different supplier's record gets 404, never the data (AC1).
/// </summary>
public sealed class GetSupplierHandler(AppDbContext db, IScopeContext scope) : IGetSupplierHandler
{
    public async Task<GetSupplierResult> HandleAsync(string referenceCode, CancellationToken ct)
    {
        if (scope.SupplierId is null)
        {
            return new GetSupplierResult.NotFoundOrOutOfScope();
        }

        var supplier = await db.Suppliers
            .Include(s => s.Representatives)
            .Where(s => s.ReferenceCode == referenceCode && s.Id == scope.SupplierId)
            .FirstOrDefaultAsync(ct);

        return supplier is null
            ? new GetSupplierResult.NotFoundOrOutOfScope()
            : new GetSupplierResult.Found(SupplierDtoMapper.ToDto(supplier));
    }

    public async Task<GetSupplierResult> HandleOwnAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null)
        {
            return new GetSupplierResult.NotFoundOrOutOfScope();
        }

        var supplier = await db.Suppliers
            .Include(s => s.Representatives)
            .FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);

        return supplier is null
            ? new GetSupplierResult.NotFoundOrOutOfScope()
            : new GetSupplierResult.Found(SupplierDtoMapper.ToDto(supplier));
    }
}
