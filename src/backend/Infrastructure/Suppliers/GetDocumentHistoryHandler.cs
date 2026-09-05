using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// SCR-132. Every version of one document type for one supplier, newest first.
///
/// <para>Resolved THROUGH the supplier named in the path and scoped by the same rule
/// <see cref="GetSupplierDocumentHandler"/> applies — the owner, or a reviewer holding
/// <c>supplier.document.review</c>. Written the same way rather than a second way, because a history
/// that authorized differently from the single-document read would be the wider of the two and
/// nobody would notice which.</para>
/// </summary>
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
            // Newest first: the current state is what a reader wants at the top, and the chain below
            // it is why it got there.
            .OrderByDescending(d => d.Version)
            .ToListAsync(ct);

        // An empty list, not a 404: the supplier and the type both exist and nothing has been
        // uploaded yet, which is a real answer and a different one from "no such type".
        return [.. versions.Select(UploadDocumentHandler.ToDto)];
    }
}
