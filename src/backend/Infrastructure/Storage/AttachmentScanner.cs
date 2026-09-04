using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Common;

namespace MotsSupplierPortal.Infrastructure.Storage;

/// <summary>
/// D-10: scans an attachment on first access when nothing has scanned it yet.
///
/// <para><b>Why on access rather than only at upload.</b> Attachments uploaded before this existed
/// carry <c>PendingScan</c>, and D-10 is explicit that they are not assumed clean. Scanning them
/// lazily means an existing tender's documents become readable as soon as someone asks for one,
/// without a backfill job that would have to walk every object in storage before anything worked.
/// New uploads are scanned here too, on their first download, for the same code path rather than a
/// second one.</para>
///
/// <para><b>The cost, stated.</b> This makes the first download of an unscanned attachment
/// synchronous on the scanner. SupplierDocument does it out-of-band through Hangfire because its
/// upload pipeline already had a job; adding one here would mean an attachment being unreadable for
/// an indeterminate period after upload, which for a tender specification a supplier is trying to
/// read is worse than a slow first request. If scan latency becomes the problem, the fix is to move
/// this to the outbox - the state field and the gate do not change.</para>
///
/// <para>Fail-closed throughout: <c>ClamAvScanner</c> already treats any scanner error as Infected,
/// and a rejected object is deleted while the row is kept as the audit trail - the same shape
/// <c>DocumentScanJob</c> uses.</para>
/// </summary>
public sealed class AttachmentScanner(IFileStorage fileStorage, IVirusScanner scanner)
{
    /// <returns><c>true</c> when the attachment is safe to serve.</returns>
    public async Task<bool> EnsureScannedAsync(
        AttachmentScanState state, string storageKey, Action markClean, Action markRejected, CancellationToken ct)
    {
        switch (state)
        {
            case AttachmentScanState.Clean:
                return true;

            case AttachmentScanState.ScanRejected:
                return false;

            default:
                await using (var content = await fileStorage.OpenReadAsync(storageKey, ct))
                {
                    var outcome = await scanner.ScanAsync(content, ct);
                    if (outcome == ScanOutcome.Infected)
                    {
                        markRejected();
                        await fileStorage.DeleteAsync(storageKey, ct);
                        return false;
                    }
                }

                markClean();
                return true;
        }
    }
}
