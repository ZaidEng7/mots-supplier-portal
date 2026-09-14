// Handing out a short-lived, audited link to one document.
//
// Readable by the owning supplier's users, or by staff who hold the document-review permission, and never
// publicly. A document still in quarantine, or one the scanner refused, is not readable by anybody.
//
//
// WHY THE PERMISSION IS CHECKED HERE RATHER THAN ON THE ROUTE
//
// This used to treat "is staff" as a stand-in for "holds document review", on the strength of a comment
// claiming the route enforced that permission. It did not: the route required only authentication, so any
// authenticated staff member of any role could download any supplier's documents.
//
// The route cannot express "owner or reviewer" as a single declarative gate, because a permission
// requirement would also block the owning supplier's own users, who legitimately hold no review
// permission. So the real check lives here, against the actual permission rather than a proxy for it.
//
// The save is what writes the access record. The audit logger no longer saves on its own and this handler
// is otherwise read-only, so without it the access-granted row would never be written, and a download that
// leaves no trace is exactly the record a review would later go looking for.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetDocumentDownloadUrlHandler(AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger) : IGetDocumentDownloadUrlHandler
{
    public async Task<DocumentDownloadUrlResult> HandleAsync(string documentCode, CancellationToken ct)
    {
        var document = await db.SupplierDocuments.FirstOrDefaultAsync(d => d.ReferenceCode == documentCode, ct);
        if (document is null)
        {
            return new DocumentDownloadUrlResult.NotFoundOrForbidden();
        }

        var isOwner = scope.SupplierId == document.SupplierId;
        var isReviewer = scope.HasPermission(Permissions.DocumentReview);
        if (!isOwner && !isReviewer)
        {
            return new DocumentDownloadUrlResult.NotFoundOrForbidden();
        }

        if (document.State is DocumentState.PendingScan or DocumentState.ScanRejected)
        {
            return new DocumentDownloadUrlResult.NotFoundOrForbidden();
        }

        var url = await fileStorage.GetSignedDownloadUrlAsync(document.StorageKey, TimeSpan.FromMinutes(5), document.OriginalFileName, ct);

        await auditLogger.LogAsync("SupplierDocument", document.Id, "document_access_granted", scope.UserId, referenceCode: document.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new DocumentDownloadUrlResult.Success(url);
    }
}
