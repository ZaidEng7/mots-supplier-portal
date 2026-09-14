// Reading one document's details, by the supplier's code and the document's code together.
//
// Resolved THROUGH the supplier named in the path rather than by the document code alone. Looking it up by
// code and then checking its parent would make the code the key, which is the classic
// direct-object-reference defect, and the same reason an attachment is resolved through its tender.
//
// The same two callers the download serves, under the same rule: the owner, or a reviewer holding the
// document-review permission. Anything else is a miss, indistinguishable from a code that does not exist.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetSupplierDocumentHandler(AppDbContext db, IScopeContext scope) : IGetSupplierDocumentHandler
{
    public async Task<SupplierDocumentDto?> HandleAsync(string supplierCode, string documentCode, CancellationToken ct)
    {
        var document = await db.SupplierDocuments.AsNoTracking()
            .Where(d => d.ReferenceCode == documentCode
                        && db.Suppliers.Any(s => s.Id == d.SupplierId && s.ReferenceCode == supplierCode))
            .FirstOrDefaultAsync(ct);
        if (document is null) return null;

        var isOwner = scope.SupplierId == document.SupplierId;
        var isReviewer = scope.HasPermission(Permissions.DocumentReview);
        if (!isOwner && !isReviewer) return null;

        return UploadDocumentHandler.ToDto(document);
    }
}
