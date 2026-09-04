using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// FR-DOC-008: authorized, audited, time-limited download. A document is readable by its owning
/// supplier's users or by staff who hold document.review - never publicly.
///
/// <para>Task #11: this used to trust "is staff" (scope.SupplierId is null) as a stand-in for
/// "holds document.review", on the strength of a comment claiming the ENDPOINT enforced that
/// permission. It did not - GetDocumentDownloadUrl was mapped with bare .RequireAuthorization(),
/// so any authenticated staff user of any role (Evaluator, ProcurementOfficer, anyone) could
/// download any supplier's documents. The endpoint can't express "owner OR reviewer" as a single
/// declarative gate (RequirePermission would ALSO block the owning supplier's own users, who
/// legitimately have no document.review permission), so the real check has to live here, against
/// the actual permission claim rather than an is-staff proxy for it.</para>
/// </summary>
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
        // MSP-64: AuditLogger no longer saves, and this handler is otherwise read-only, so
        // without this the access-granted row would never be written. A download that leaves no
        // audit trace is exactly the record a review would later go looking for.
        await db.SaveChangesAsync(ct);

        return new DocumentDownloadUrlResult.Success(url);
    }
}
