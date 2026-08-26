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
            .Where(s => s.ReferenceCode == referenceCode && s.Id == scope.SupplierId)
            .Select(s => new SupplierDto(s.ReferenceCode, s.DisplayNameAr, s.DisplayNameEn, s.OnboardingState.ToString()))
            .FirstOrDefaultAsync(ct);

        return supplier is null
            ? new GetSupplierResult.NotFoundOrOutOfScope()
            : new GetSupplierResult.Found(supplier);
    }
}
