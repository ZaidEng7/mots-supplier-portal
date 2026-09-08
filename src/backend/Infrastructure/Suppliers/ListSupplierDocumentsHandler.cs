using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class ListSupplierDocumentsHandler(AppDbContext db, IScopeContext scope) : IListSupplierDocumentsHandler
{
    public async Task<IReadOnlyList<DocumentTypeStatusDto>> HandleOwnAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return [];
        return await BuildAsync(db, scope.SupplierId.Value, ct);
    }

    /// <summary>MSP-84: deliberately not cursor-paginated like Review Queue/Team Members/Sessions.
    /// This list is one row per active DocumentType - an admin-managed reference table (3 seeded
    /// rows today, no CRUD endpoint that could grow it), not user-generated content. See
    /// Tests/Integration/OwnDocumentsDenominatorTests.cs for the denominator assertion that stands
    /// in for pagination here: it proves every active type is returned, not that the response is
    /// windowed.</summary>
    internal static async Task<IReadOnlyList<DocumentTypeStatusDto>> BuildAsync(AppDbContext db, Guid supplierId, CancellationToken ct)
    {
        var types = await db.DocumentTypes.Where(t => t.IsActive).OrderBy(t => t.Code).ToListAsync(ct);
        var latestDocs = await db.SupplierDocuments
            .Where(d => d.SupplierId == supplierId && d.IsLatestVersion)
            .ToListAsync(ct);

        // BRULE-016, live since D-59: `required` is per supplier, not per type. Every active type is still
        // listed - a supplier may upload something nobody demanded of them - but the flag this checklist
        // draws its "Required" badge from is now the same answer the submit gate refuses on, resolved by
        // the same function. A checklist that says required where the gate says otherwise is the specific
        // failure this shares its resolver to avoid.
        var requiredIds = await RequiredDocumentTypeResolver.RequiredIdsAmongAsync(db, supplierId, types, ct);

        return [.. types.Select(t =>
        {
            var latest = latestDocs.FirstOrDefault(d => d.DocumentTypeId == t.Id);
            return new DocumentTypeStatusDto(
                t.Id, t.Code, t.NameAr, t.NameEn, requiredIds.Contains(t.Id), t.ExpiryTracked,
                latest is null ? null : UploadDocumentHandler.ToDto(latest));
        })];
    }
}

