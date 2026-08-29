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
            .IncludeProfile()
            .Where(s => s.ReferenceCode == referenceCode && s.Id == scope.SupplierId)
            .FirstOrDefaultAsync(ct);

        if (supplier is null)
        {
            return new GetSupplierResult.NotFoundOrOutOfScope();
        }

        // BRULE-018: computed on read rather than stored, so it cannot drift from the documents it
        // describes. This is the path that makes an expiry visible on an ALREADY-APPROVED supplier -
        // the submit and approval gates are both pre-approval and never run again.
        var incomplete = await DocumentCompletenessEvaluator
            .GetProfileIncompleteDocumentTypeCodesAsync(db, supplier.Id, ct);

        return new GetSupplierResult.Found(SupplierDtoMapper.ToDto(supplier, incomplete));
    }

    public async Task<GetSupplierResult> HandleOwnAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null)
        {
            return new GetSupplierResult.NotFoundOrOutOfScope();
        }

        var supplier = await db.Suppliers
            .IncludeProfile()
            .FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);

        if (supplier is null)
        {
            return new GetSupplierResult.NotFoundOrOutOfScope();
        }

        // BRULE-018: computed on read rather than stored, so it cannot drift from the documents it
        // describes. This is the path that makes an expiry visible on an ALREADY-APPROVED supplier -
        // the submit and approval gates are both pre-approval and never run again.
        var incomplete = await DocumentCompletenessEvaluator
            .GetProfileIncompleteDocumentTypeCodesAsync(db, supplier.Id, ct);

        return new GetSupplierResult.Found(SupplierDtoMapper.ToDto(supplier, incomplete));
    }
}
