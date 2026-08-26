using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// The AV scan step of the upload pipeline (docs/security/SECURITY-ARCHITECTURE.md §4.1): runs
/// out-of-band via Hangfire so the upload request doesn't block on the scan. Clean -> move the
/// object out of quarantine and the document becomes visible/downloadable. Infected -> delete the
/// object; the row is kept as ScanRejected purely as an audit trail.
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
            await auditLogger.LogAsync("SupplierDocument", document.Id, "document_scan_rejected", Guid.NewGuid(), ct: ct);
        }
        else
        {
            var cleanKey = quarantineKey.Replace("quarantine/", "clean/", StringComparison.Ordinal);
            await fileStorage.MoveAsync(quarantineKey, cleanKey, ct);
            document.MarkScanClean(cleanKey);
            await auditLogger.LogAsync("SupplierDocument", document.Id, "document_scan_clean", Guid.NewGuid(), ct: ct);
        }

        await db.SaveChangesAsync(ct);
    }
}
