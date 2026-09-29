// Scanning an attachment on first access, when nothing has scanned it yet.
//
//
// WHY ON ACCESS RATHER THAN ONLY AT UPLOAD
//
// Attachments uploaded before this existed carry the unscanned state, and the recorded decision is explicit that
// they are not assumed clean.
//
// Scanning them lazily means an existing tender's documents become readable as soon as somebody asks for one,
// without a backfill job that would have to walk every object in storage before anything worked.
//
// New uploads are scanned here too, on their first download, so there is one code path rather than two.
//
//
// THE COST, STATED
//
// This makes the first download of an unscanned attachment wait on the scanner.
//
// Supplier documents are scanned out of band because their upload pipeline already had a job. Adding one here
// would mean an attachment being unreadable for an indeterminate period after upload, which for a tender
// specification a supplier is trying to read is worse than a slow first request.
//
// If scan latency ever becomes the problem, the fix is to move this onto the outbox. The state field and the gate
// do not change.
//
// Fail-closed throughout, without destroying anything: an infected object is deleted while the row is kept as the
// record that it happened, which is the same shape the document scan job uses; an object the scanner could not
// judge is neither served nor deleted, and the reader is told to try again. That second case used to be the first -
// the scanner reported any error as infected - so a scanner outage deleted the tender specification of whoever
// happened to open it first.

namespace MotsSupplierPortal.Infrastructure.Storage;

using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Common;

public enum AttachmentScanVerdict
{
    Safe,
    Rejected,
    ScannerUnavailable,
}

public sealed class AttachmentScanner(IFileStorage fileStorage, IVirusScanner scanner)
{
    public async Task<AttachmentScanVerdict> EnsureScannedAsync(
        AttachmentScanState state, string storageKey, Action markClean, Action markRejected, CancellationToken ct)
    {
        switch (state)
        {
            case AttachmentScanState.Clean:
                return AttachmentScanVerdict.Safe;

            case AttachmentScanState.ScanRejected:
                return AttachmentScanVerdict.Rejected;

            default:
                await using (var content = await fileStorage.OpenReadAsync(storageKey, ct))
                {
                    var outcome = await scanner.ScanAsync(content, ct);
                    if (outcome == ScanOutcome.Unavailable)
                    {
                        return AttachmentScanVerdict.ScannerUnavailable;
                    }

                    if (outcome == ScanOutcome.Infected)
                    {
                        markRejected();
                        await fileStorage.DeleteAsync(storageKey, ct);
                        return AttachmentScanVerdict.Rejected;
                    }
                }

                markClean();
                return AttachmentScanVerdict.Safe;
        }
    }
}
