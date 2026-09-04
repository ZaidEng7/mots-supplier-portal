using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>T-012: see IGetSupplierDocumentHandler for why this exists.</summary>
public sealed class GetSupplierDocumentHandler(AppDbContext db, IScopeContext scope) : IGetSupplierDocumentHandler
{
    public async Task<SupplierDocumentDto?> HandleAsync(string supplierCode, string documentCode, CancellationToken ct)
    {
        // Resolved THROUGH the supplier named in the path, not by the document code alone. Looking it
        // up by code and then checking the parent would make the code the key - the classic
        // direct-object read defect, and the same reason GetRfqAttachmentDownloadUrlHandler resolves
        // its attachment through the RFQ.
        var document = await db.SupplierDocuments.AsNoTracking()
            .Where(d => d.ReferenceCode == documentCode
                        && db.Suppliers.Any(s => s.Id == d.SupplierId && s.ReferenceCode == supplierCode))
            .FirstOrDefaultAsync(ct);
        if (document is null) return null;

        // The same two callers the download serves, and the same rule: the owner, or a reviewer
        // holding document.review. Anything else is a miss, indistinguishable from a code that does
        // not exist (§9.2).
        var isOwner = scope.SupplierId == document.SupplierId;
        var isReviewer = scope.HasPermission(Permissions.DocumentReview);
        if (!isOwner && !isReviewer) return null;

        return UploadDocumentHandler.ToDto(document);
    }
}
