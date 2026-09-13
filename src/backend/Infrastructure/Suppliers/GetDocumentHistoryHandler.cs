// Every version of one document type for one supplier, newest first.
//
// Resolved through the supplier named in the path and scoped by the same rule the single-document read
// applies: the owner, or a reviewer holding the document-review permission.
//
// Written the same way rather than a second way, because a history that authorised differently from the
// single read would be the wider of the two and nobody would notice which.
//
// Newest first, because the current state is what a reader wants at the top and the chain below it is why it
// got there.
//
// A supplier and type that exist with nothing uploaded yet come back as an empty list rather than a
// not-found. That is a real answer, and a different one from "no such type".

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetDocumentHistoryHandler(AppDbContext db, IScopeContext scope) : IGetDocumentHistoryHandler
{
    public async Task<IReadOnlyList<SupplierDocumentDto>?> HandleAsync(
        string supplierCode, string documentTypeCode, CancellationToken ct)
    {
        var supplierId = await db.Suppliers.AsNoTracking()
            .Where(s => s.ReferenceCode == supplierCode)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (supplierId is null) return null;

        var isOwner = scope.SupplierId == supplierId;
        var isReviewer = scope.HasPermission(Permissions.DocumentReview);
        if (!isOwner && !isReviewer) return null;

        var typeId = await db.DocumentTypes.AsNoTracking()
            .Where(t => t.Code == documentTypeCode)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);
        if (typeId is null) return null;

        var versions = await db.SupplierDocuments.AsNoTracking()
            .Where(d => d.SupplierId == supplierId && d.DocumentTypeId == typeId)
            .OrderByDescending(d => d.Version)
            .ToListAsync(ct);

        return [.. versions.Select(UploadDocumentHandler.ToDto)];
    }
}
