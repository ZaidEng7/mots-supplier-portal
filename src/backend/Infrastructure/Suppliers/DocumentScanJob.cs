// The virus scan step of the upload pipeline.
//
// It runs out of band so the upload request does not wait on the scan.
//
// Clean: the file moves out of the quarantine area, the document becomes visible and downloadable, and it
// enters the review queue.
//
// Infected: the file is deleted, and the row is kept as refused purely as a record that it happened.
//
// Scanner unavailable: nothing changes. The file stays in quarantine, the document stays pending, and the job fails
// so the job server tries it again later. It used to be treated as infected, which deleted the supplier's upload
// because the scanner happened to be down; a supplier who replaced an expired licence during an outage lost it and
// was never told. If every retry runs out the document is still pending and its file still in quarantine, where it
// can be scanned again once the scanner is back.
//
// Already scanned: nothing happens. The job server runs a job at least once rather than exactly once, and runs a
// failed one again, so a run can find the document already scanned: refused, or clean and anywhere in review since.
// A document in any state but PendingScan stops the run as soon as the row is read, before the file is read or the
// scanner asked, and the run logs that it skipped. It used to read the file and ask the scanner first and meet the
// state only afterwards. A document scanned clean had its file read and the scanner asked, and then the job failed
// on the answer. A clean answer tried to move the clean file onto itself; the move is a copy and then a delete of the
// source, which is the same object, and only the object store's refusal to copy an object onto itself kept that from
// deleting the supplier's file. An infected answer was turned away by the document for its state. A refused
// document failed sooner, on reading its file, which was deleted when it was refused, so the scanner was never
// asked. Either way the job failed, so the job server ran it again, failing the same way each time, until its
// retries ran out.
//
// The written architecture describes THIS job as the thing that moves a document into review. It used to
// stop one state short, which is why the documented reviewer queue returned nothing. Both transitions land
// in the same save, so a reviewer never observes the intermediate state; only a crash between them does.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class DocumentScanJob(
    AppDbContext db, IFileStorage fileStorage, IVirusScanner scanner, IAuditLogger auditLogger, ILogger<DocumentScanJob> logger)
{
    public async Task ScanAsync(Guid documentId, CancellationToken ct)
    {
        var document = await db.SupplierDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null) return;

        if (document.State != DocumentState.PendingScan)
        {
            logger.LogInformation(
                "Virus scan of document {ReferenceCode} skipped: it is {State}, not PendingScan, so an earlier run "
                + "already scanned it.",
                document.ReferenceCode, document.State);
            return;
        }

        var quarantineKey = document.StorageKey;
        await using var stream = await fileStorage.OpenReadAsync(quarantineKey, ct);
        var outcome = await scanner.ScanAsync(stream, ct);

        if (outcome == ScanOutcome.Unavailable)
        {
            throw new VirusScannerUnavailableException();
        }

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
