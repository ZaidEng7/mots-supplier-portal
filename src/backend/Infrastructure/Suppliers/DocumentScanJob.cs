using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// The AV scan step of the upload pipeline (docs/security/SECURITY-ARCHITECTURE.md §4.1): runs
/// out-of-band via Hangfire so the upload request doesn't block on the scan. Clean -> move the
/// object out of quarantine, the document becomes visible/downloadable, and it enters the review
/// queue (UnderReview). Infected -> delete the object; the row is kept as ScanRejected purely as an
/// audit trail.
/// </summary>
public sealed class DocumentScanJob(AppDbContext db, IFileStorage fileStorage, IVirusScanner scanner, IAuditLogger auditLogger)
{
    public async Task ScanAsync(Guid documentId, CancellationToken ct)
    {
        var document = await db.SupplierDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null) return;

        var quarantineKey = document.StorageKey;
        await using var stream = await fileStorage.OpenReadAsync(quarantineKey, ct);
        var outcome = await scanner.ScanAsync(stream, ct);

        if (outcome == ScanOutcome.Infected)
        {
            document.MarkScanRejected();
            await fileStorage.DeleteAsync(quarantineKey, ct);
            await auditLogger.LogAsync("SupplierDocument", document.Id, "document_scan_rejected", referenceCode: document.ReferenceCode, ct: ct);
        }
        else
        {
            var cleanKey = quarantineKey.Replace("quarantine/", "clean/", StringComparison.Ordinal);
            await fileStorage.MoveAsync(quarantineKey, cleanKey, ct);
            document.MarkScanClean(cleanKey);
            // API-ARCHITECTURE.md §4.4 describes THIS job as the thing that transitions a document
            // to UnderReview ("an async Hangfire job runs virus scan + validation, transitioning to
            // UnderReview"). It stopped at Uploaded, which is why the documented reviewer queue
            // returned nothing (T-052). Both transitions land in the same SaveChanges below, so a
            // reviewer never observes the intermediate state - only a crash between them does.
            document.EnterReview();
            await auditLogger.LogAsync("SupplierDocument", document.Id, "document_scan_clean", referenceCode: document.ReferenceCode, ct: ct);
        }

        await db.SaveChangesAsync(ct);
    }
}
