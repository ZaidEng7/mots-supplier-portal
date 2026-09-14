// Reading one supplier's profile, either as that supplier or as a reviewer.
//
// The scope is applied inside the query rather than filtered afterwards. A supplier user asking for another
// supplier's record gets a not-found, never the data.
//
//
// THREE FIGURES COMPUTED ON READ RATHER THAN STORED
//
// The documents flagging the profile incomplete are computed here, so they cannot drift from the documents
// they describe. This is also the only path that makes an expiry visible on an ALREADY-APPROVED supplier,
// because the submit gate and the approval gate both run before approval and never run again.
//
// The completeness fraction is the same two lists the submit gate refuses on, expressed as a fraction. So a
// supplier reading a full meter can submit, and one below it is looking at exactly what is stopping them.
// Its denominator is the set THIS supplier is required to hold, resolved by the same function the submit
// gate asks, so the fraction and the refusal cannot disagree about what complete means.
//
// The document summary is on the read path for the same reason.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

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

        var incomplete = await DocumentCompletenessEvaluator
            .GetProfileIncompleteDocumentTypeCodesAsync(db, supplier.Id, ct);

        var missingDocumentTypes = await DocumentCompletenessEvaluator
            .GetMissingRequiredDocumentTypeCodesAsync(db, supplier.Id, ct);
        var requiredDocumentTypeCount =
            (await RequiredDocumentTypeResolver.ForSupplierAsync(db, supplier.Id, ct)).Count;

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

        var incomplete = await DocumentCompletenessEvaluator
            .GetProfileIncompleteDocumentTypeCodesAsync(db, supplier.Id, ct);

        var missingDocumentTypes = await DocumentCompletenessEvaluator
            .GetMissingRequiredDocumentTypeCodesAsync(db, supplier.Id, ct);
        var requiredDocumentTypeCount =
            (await RequiredDocumentTypeResolver.ForSupplierAsync(db, supplier.Id, ct)).Count;

        var documentsSummary = await DocumentCompletenessEvaluator.GetDocumentsSummaryAsync(db, supplier.Id, ct);

        return new GetSupplierResult.Found(
            SupplierDtoMapper.ToDto(supplier, incomplete, missingDocumentTypes, requiredDocumentTypeCount, documentsSummary));
    }
}
