// Putting the virus scans that never finished back in the queue, and what the operator is told afterwards.
//
// A supplier's document waits in PendingScan until its scan job answers. A scan that ran out of retries while the
// scanner was down, or a job that was never queued at all, leaves the document there for good, and the supplier sees
// it as pending with nothing they can do about it. The administrator can re-queue those scans from the operations
// screen.
//
// A document counts as stuck once it has been pending for longer than PendingLongerThan. One call requeues at most
// BatchSize of them, the oldest first, so a backlog is cleared over several presses rather than in one long request.
// Documents it cannot requeue do not take up the batch: it looks further, up to ten batches' worth, as
// RetryStuckScansHandler describes.
// The dashboard's stuck-scan figure means the same thing, so the two read the same threshold from here.
//
// Requeued is how many scans this call put back in the queue. StillPending is how many stuck documents it left as
// they were: those whose scan is already queued or running, those whose file is no longer in quarantine, and those
// the call did not reach. QuarantineFileMissing names, by reference code, the documents this call examined whose
// file is no longer in quarantine. A new scan of one of those would fail on reading the file, so they are reported for a
// person to look at rather than re-queued.
//
// The handler is given the caller's identifier rather than reading it from the scope, because it names the person on
// the audit rows and nothing else; the endpoint refuses a caller without one.

namespace MotsSupplierPortal.Application.Admin;

public static class StuckScans
{
    public static readonly TimeSpan PendingLongerThan = TimeSpan.FromMinutes(15);

    public const int BatchSize = 100;
}

public sealed record RetryStuckScansResultDto(
    int Requeued,
    int StillPending,
    IReadOnlyList<string> QuarantineFileMissing);

public interface IRetryStuckScansHandler
{
    Task<RetryStuckScansResultDto> HandleAsync(Guid actorUserId, CancellationToken ct);
}
