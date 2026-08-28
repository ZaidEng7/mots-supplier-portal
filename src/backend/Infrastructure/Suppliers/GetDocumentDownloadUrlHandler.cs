using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// FR-DOC-008: authorized, audited, time-limited download. A document is readable by its owning
/// supplier's users or by staff (endpoint-gated by document.review permission) - never publicly.
/// </summary>
public sealed class GetDocumentDownloadUrlHandler(AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger) : IGetDocumentDownloadUrlHandler
{
    public async Task<DocumentDownloadUrlResult> HandleAsync(Guid documentId, CancellationToken ct)
    {
        var document = await db.SupplierDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return new DocumentDownloadUrlResult.NotFoundOrForbidden();
        }

        // Owning supplier's own users may always see their own documents; staff access is gated
        // by the document.review permission at the endpoint (scope.SupplierId is null for staff).
        var isOwner = scope.SupplierId == document.SupplierId;
        var isStaff = scope.SupplierId is null;
        if (!isOwner && !isStaff)
        {
            return new DocumentDownloadUrlResult.NotFoundOrForbidden();
        }

        if (document.State is DocumentState.PendingScan or DocumentState.ScanRejected)
        {
            return new DocumentDownloadUrlResult.NotFoundOrForbidden();
        }

        var url = await fileStorage.GetSignedDownloadUrlAsync(document.StorageKey, TimeSpan.FromMinutes(5), document.OriginalFileName, ct);

        await auditLogger.LogAsync("SupplierDocument", document.Id, "document_access_granted", scope.UserId, ct: ct);
        // MSP-64: AuditLogger no longer saves, and this handler is otherwise read-only, so
        // without this the access-granted row would never be written. A download that leaves no
        // audit trace is exactly the record a review would later go looking for.
        await db.SaveChangesAsync(ct);

        return new DocumentDownloadUrlResult.Success(url);
    }
}
