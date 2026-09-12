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

        // §12.2's profileCompleteness. The same two lists the SUBMIT GATE refuses on, expressed as
        // a fraction - so a supplier reading 100% can submit, and one below it is looking at exactly
        // what is stopping them.
        var missingDocumentTypes = await DocumentCompletenessEvaluator
            .GetMissingRequiredDocumentTypeCodesAsync(db, supplier.Id, ct);
        // BRULE-016, live since D-59. The denominator is the set THIS supplier is required to hold,
        // resolved by RequiredDocumentTypeResolver - the same function the submit gate asks, so the
        // fraction and the refusal cannot disagree about what "complete" means.
        var requiredDocumentTypeCount =
            (await RequiredDocumentTypeResolver.ForSupplierAsync(db, supplier.Id, ct)).Count;

        // T-002/§12.2's documentsSummary, on the read path for the same reason as the fraction above.
        var documentsSummary = await DocumentCompletenessEvaluator.GetDocumentsSummaryAsync(db, supplier.Id, ct);

        return new GetSupplierResult.Found(
            SupplierDtoMapper.ToDto(supplier, incomplete, missingDocumentTypes, requiredDocumentTypeCount, documentsSummary));
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

        // §12.2's profileCompleteness. The same two lists the SUBMIT GATE refuses on, expressed as
        // a fraction - so a supplier reading 100% can submit, and one below it is looking at exactly
        // what is stopping them.
        var missingDocumentTypes = await DocumentCompletenessEvaluator
            .GetMissingRequiredDocumentTypeCodesAsync(db, supplier.Id, ct);
        var requiredDocumentTypeCount =
            (await RequiredDocumentTypeResolver.ForSupplierAsync(db, supplier.Id, ct)).Count;

        var documentsSummary = await DocumentCompletenessEvaluator.GetDocumentsSummaryAsync(db, supplier.Id, ct);

        return new GetSupplierResult.Found(
            SupplierDtoMapper.ToDto(supplier, incomplete, missingDocumentTypes, requiredDocumentTypeCount, documentsSummary));
    }
}
