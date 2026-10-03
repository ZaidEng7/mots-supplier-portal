// Re-queues the virus scans of supplier documents that have been pending for too long.
//
// A supplier's upload waits in PendingScan until its scan job answers. When the scanner is down the job fails, the job
// server retries it ten times, and after the last retry the job is left failed and the document pending for good.
// A job that was never queued, because the process stopped between saving the upload and queuing its scan, leaves
// the same document behind with no job at all. This is the operator's way out of both. Only supplier documents are
// covered: tender attachments and bid files are scanned when first downloaded rather than by a job, so a reader
// asking for one starts its scan again.
//
//
// WHICH DOCUMENTS, AND IN WHAT ORDER
//
// Documents pending for longer than StuckScans.PendingLongerThan, at most StuckScans.BatchSize of them, the oldest
// first. The response's still-pending figure counts every stuck document this call left as it was, including those
// past the batch, so an operator can tell whether to press again.
//
//
// WHAT HAPPENS TO EACH ONE
//
// A document whose scan job is enqueued or running is skipped, because a scan is already on its way.
//
// Otherwise its scan jobs that are failed, or scheduled to retry, are deleted and one new scan is queued. Deleting
// them first means the document ends with exactly one scan job waiting rather than a new one beside a retry that
// would scan it again later. Each delete names the state the job was read in, so a retry that moved on to the queue
// in the meantime is not deleted; the document then already has a scan on its way and is not given a second one. A
// second scan would do no harm, because the scan job leaves a document alone once it is past PendingScan, but it
// would be wasted work against a scanner that has only just come back.
//
// Each requeue writes a document_scan_requeued row naming the caller. The supplier's own trail leaves that action
// out, because it is staff work on the scanning queue rather than a change to the supplier's file.
//
//
// A FILE NO LONGER IN QUARANTINE IS REPORTED, NOT REQUEUED
//
// The scan job moves a clean file out of quarantine and then saves the document. If the move succeeded and the save
// failed, the row still says PendingScan and still points at the quarantine key, and the file now lives under the
// clean key. A new scan would fail on reading the quarantine key and be retried ten times for nothing. So every
// document's quarantine file is checked first, and those whose file is gone are named in the response for a person
// to look at.
//
//
// NOTHING CHANGES UNTIL EVERY CHECK HAS RUN
//
// The job server and the object store are read first, for the whole batch, and only then are jobs deleted and
// queued. If the store fails to answer, the request fails before anything was changed.
//
// The audit rows are saved once, at the end, because the audit logger adds rows and leaves the saving to its caller.
// That save ignores the request's cancellation: by then the scans are already queued, and a caller who went away
// must not leave queued scans with no record of who queued them. The job server's storage is a separate transaction,
// so if the save itself fails the scans still run unrecorded. Recording them before queuing would leave the opposite
// gap, rows claiming requeues that never happened.
//
//
// THE JOB SERVER IS READ THROUGH THIS HOST'S OWN STORAGE
//
// Through the storage this host was given rather than the process-wide static facade, which in a process running
// more than one host answers for whichever host started first. Its monitoring lists are paged, and only the four
// states that matter are read. The queued list is read for every queue the storage holds rather than only the
// default one, so a scan queued anywhere counts as on its way.
//
//
// IT READS EVERY SUPPLIER'S DOCUMENTS, BY DESIGN
//
// This is platform administration, gated on the user-management permission, and the scanning queue is one queue for
// the deployment. Scoping it to the caller would find nothing, because an administrator belongs to no supplier and
// no organisation. So it is listed in the row-scope guard's exemptions with that reason. The caller is named on the
// audit rows through the identifier the endpoint passes in, rather than by reading the scope here, so the guard sees
// this handler for what it is: an unscoped read, on purpose.

namespace MotsSupplierPortal.Infrastructure.Admin;

using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage.Monitoring;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class RetryStuckScansHandler(
    AppDbContext db,
    JobStorage jobStorage,
    IBackgroundJobClient backgroundJobs,
    IFileStorage fileStorage,
    IAuditLogger auditLogger) : IRetryStuckScansHandler
{
    private const int MonitoringPageSize = 500;

    private sealed record StuckDocument(Guid Id, string ReferenceCode, string StorageKey);

    private sealed record ScanJob(Guid DocumentId, string JobId, string State, bool InFlight);

    public async Task<RetryStuckScansResultDto> HandleAsync(Guid actorUserId, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - StuckScans.PendingLongerThan;
        var stuck = db.SupplierDocuments.AsNoTracking()
            .Where(d => d.State == DocumentState.PendingScan && d.UploadedAt < cutoff);

        var stuckCount = await stuck.CountAsync(ct);
        var batch = await stuck
            .OrderBy(d => d.UploadedAt).ThenBy(d => d.Id)
            .Take(StuckScans.BatchSize)
            .Select(d => new StuckDocument(d.Id, d.ReferenceCode, d.StorageKey))
            .ToListAsync(ct);

        var scanJobs = ScanJobsOf(batch.Select(d => d.Id).ToHashSet());

        var requeueable = new List<(StuckDocument Document, List<ScanJob> DeadJobs)>();
        var quarantineFileMissing = new List<string>();
        foreach (var document in batch)
        {
            var jobs = scanJobs[document.Id].ToList();
            if (jobs.Any(job => job.InFlight)) continue;

            if (!await fileStorage.ExistsAsync(document.StorageKey, ct))
            {
                quarantineFileMissing.Add(document.ReferenceCode);
                continue;
            }

            requeueable.Add((document, jobs));
        }

        var requeued = 0;
        foreach (var (document, deadJobs) in requeueable)
        {
            if (!deadJobs.All(job => backgroundJobs.Delete(job.JobId, job.State))) continue;

            backgroundJobs.Enqueue<DocumentScanJob>(job => job.ScanAsync(document.Id, CancellationToken.None));
            await auditLogger.LogAsync(
                "SupplierDocument", document.Id, "document_scan_requeued", actorUserId,
                referenceCode: document.ReferenceCode, ct: CancellationToken.None);
            requeued++;
        }

        await db.SaveChangesAsync(CancellationToken.None);

        return new RetryStuckScansResultDto(requeued, stuckCount - requeued, quarantineFileMissing);
    }

    private ILookup<Guid, ScanJob> ScanJobsOf(IReadOnlySet<Guid> documentIds)
    {
        var found = new List<ScanJob>();
        if (documentIds.Count == 0) return found.ToLookup(job => job.DocumentId);

        var monitoring = jobStorage.GetMonitoringApi();

        foreach (var queue in monitoring.Queues())
        {
            Collect(EnqueuedState.StateName, inFlight: true, Pages((from, count) => monitoring.EnqueuedJobs(queue.Name, from, count))
                .Where(row => row.Value.InEnqueuedState)
                .Select(row => (row.Key, row.Value.Job)));
        }

        Collect(ProcessingState.StateName, inFlight: true, Pages(monitoring.ProcessingJobs)
            .Where(row => row.Value.InProcessingState)
            .Select(row => (row.Key, row.Value.Job)));

        Collect(ScheduledState.StateName, inFlight: false, Pages(monitoring.ScheduledJobs)
            .Where(row => row.Value.InScheduledState)
            .Select(row => (row.Key, row.Value.Job)));

        Collect(FailedState.StateName, inFlight: false, Pages(monitoring.FailedJobs)
            .Where(row => row.Value.InFailedState)
            .Select(row => (row.Key, row.Value.Job)));

        return found.ToLookup(job => job.DocumentId);

        void Collect(string state, bool inFlight, IEnumerable<(string JobId, Job Job)> rows)
        {
            foreach (var (jobId, job) in rows)
            {
                if (ScannedDocumentOf(job) is { } documentId && documentIds.Contains(documentId))
                {
                    found.Add(new ScanJob(documentId, jobId, state, inFlight));
                }
            }
        }
    }

    private static Guid? ScannedDocumentOf(Job? job) =>
        job is not null
        && job.Type == typeof(DocumentScanJob)
        && job.Method.Name == nameof(DocumentScanJob.ScanAsync)
        && job.Args.Count > 0
        && job.Args[0] is Guid documentId
            ? documentId
            : null;

    private static IEnumerable<KeyValuePair<string, T>> Pages<T>(Func<int, int, JobList<T>> page)
    {
        for (var from = 0; ; from += MonitoringPageSize)
        {
            var rows = page(from, MonitoringPageSize);
            foreach (var row in rows) yield return row;
            if (rows.Count < MonitoringPageSize) yield break;
        }
    }
}
