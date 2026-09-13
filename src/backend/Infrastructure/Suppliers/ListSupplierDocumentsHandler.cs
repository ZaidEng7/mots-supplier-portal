// A supplier's own document checklist: one row per active document type, with the latest upload against it.
//
// Deliberately not paged like the queue, the team list and the sessions list. This is one row per active
// document type, which is an administrator-managed reference table with a handful of rows and no endpoint
// that could grow it, rather than content a user generates.
//
// A test asserting the denominator stands in for paging here: it proves every active type is returned,
// rather than that the response is windowed.
//
// Every active type is still listed, because a supplier may upload something nobody demanded of them, but
// the flag this checklist draws its "required" badge from is the same answer the submit gate refuses on,
// resolved by the same function. A checklist saying required where the gate says otherwise is the specific
// failure that sharing the resolver avoids.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ListSupplierDocumentsHandler(AppDbContext db, IScopeContext scope) : IListSupplierDocumentsHandler
{
    public async Task<IReadOnlyList<DocumentTypeStatusDto>> HandleOwnAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return [];
        return await BuildAsync(db, scope.SupplierId.Value, ct);
    }

    internal static async Task<IReadOnlyList<DocumentTypeStatusDto>> BuildAsync(AppDbContext db, Guid supplierId, CancellationToken ct)
    {
        var types = await db.DocumentTypes.Where(t => t.IsActive).OrderBy(t => t.Code).ToListAsync(ct);
        var latestDocs = await db.SupplierDocuments
            .Where(d => d.SupplierId == supplierId && d.IsLatestVersion)
            .ToListAsync(ct);

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
