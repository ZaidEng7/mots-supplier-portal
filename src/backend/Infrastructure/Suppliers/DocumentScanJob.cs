// The virus scan step of the upload pipeline.
//
// It runs out of band so the upload request does not wait on the scan.
//
// Clean: the file moves out of the quarantine area, the document becomes visible and downloadable, and it
// enters the review queue.
//
// Infected: the file is deleted, and the row is kept as refused purely as a record that it happened.
//
// The written architecture describes THIS job as the thing that moves a document into review. It used to
// stop one state short, which is why the documented reviewer queue returned nothing. Both transitions land
// in the same save, so a reviewer never observes the intermediate state; only a crash between them does.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

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
            document.EnterReview();
            await auditLogger.LogAsync("SupplierDocument", document.Id, "document_scan_clean", referenceCode: document.ReferenceCode, ct: ct);
        }

        await db.SaveChangesAsync(ct);
    }
}
