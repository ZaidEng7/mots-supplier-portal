// The system health section of the administrator's dashboard: the jobs, the queue, mail, the outbox, scans,
// migrations, reference lists, the purchase-order transport and the object store.
//
// Every figure is state that already exists, read from the database, the job scheduler's own storage or the
// configuration. The one outside call is a ping of the object store, capped at five seconds. The ERP is never
// contacted from here; its block is a section of its own.
//
//
// EVERY THRESHOLD TRAVELS WITH ITS FIGURE
//
// A count of late jobs or stuck scans means nothing without the line it was measured against, so each one carries
// its threshold in minutes, or its window in days. The screen says "older than 15 minutes" from the answer rather
// than from a copy of the number of its own, which could drift from the one the server used.
//
//
// A JOB'S VERDICT IS ONE WORD, AND ITS EVIDENCE COMES WITH IT
//
// One row for each of the eight recurring jobs this application schedules, in the order of RecurringJobs.All,
// whether or not the scheduler holds it:
//
//   disabled   recurring jobs are switched off for the whole deployment, so no job runs on a timer
//   missing    the switch is on and the scheduler does not hold this job
//   late       the job has not run for longer than its threshold
//   failed     its last run failed
//   retrying   its last run is waiting to be tried again, which the scheduler records as Scheduled
//   ok         none of the above
//
// They are checked in that order, so a job that is late and whose last run also failed is late: the scheduler has
// stopped starting it, and that is the bigger fault. The last state, the last run and the next run are carried
// beside the verdict in the scheduler's own words, so the screen can show why.
//
// Late is measured from the last run. The five-minute jobs are late after 15 minutes, the hourly ones after 3
// hours and the daily ones after 26 hours. A job that has never run is late once its first run is overdue by
// more than the grace those thresholds allow beyond the schedule: 10 minutes for the five-minute jobs and 2 hours
// for the others.
//
// The link is where the screen sends somebody about that job: the import's own page for the hourly supplier
// import, for a viewer who holds supplier.import.run, and the operations page for everything else.
//
//
// THE QUEUE IS THE SCHEDULER'S OWN COUNT, WITH ONE CORRECTION
//
// Retrying is every job waiting in the scheduler's Scheduled state, less the hourly import's own second try. When
// the import finds the lock taken it schedules one more attempt two minutes later, which waits in the same state
// as a retry and is not one.
//
// A live server is one that has reported in within the last 5 minutes. The scheduler keeps a row for a server
// that has stopped until its own clean-up removes it, so a count of rows would include servers that are gone.
//
// Mail is counted from the scheduler too, because an email is a background job: the ones that failed in the
// last 7 days, and the ones waiting to be tried again.
//
//
// THE REST
//
// The outbox carries what is pending, what has been pending for longer than 15 minutes, what failed, and when
// the oldest pending message was written. Stuck scans are supplier documents still waiting for the virus scanner
// 15 minutes after upload. Tender and bid files are scanned on another path and are not counted here.
//
// Pending migrations are named rather than counted, because the name is what a person applying them by hand
// needs.
//
// The reference lists carry, for each table, how many codes are active and how many are switched off. A table
// with no active codes blocks registration.
//
// The purchase-order transport is named for what it carries. While it is the logging stand-in, an award's
// purchase order is logged and sent nowhere. It says nothing about the supplier import and push, which reach the
// ERP over a connection of their own. Failed sends are the awards whose purchase order could not be sent. No
// award amount appears anywhere on the dashboard.
//
// The object store is reachable when it answered the readiness probe within five seconds. The virus scanner is
// not probed here, because a scan is slower and heavier than a ping and this answer is fetched every minute. It
// is probed on demand, through the storage probe beside this section.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

using System.Text.Json.Serialization;

public sealed record DashboardSystemHealthDto(
    DashboardJobsDto Jobs,
    DashboardJobQueueDto Queue,
    DashboardEmailDto Email,
    DashboardOutboxDto Outbox,
    DashboardStuckScansDto Scans,
    IReadOnlyList<string> PendingMigrations,
    IReadOnlyList<ReferenceTableHealthDto> ReferenceLists,
    DashboardPurchaseOrderTransportDto PurchaseOrderTransport,
    DashboardObjectStorageDto ObjectStorage);

public enum DashboardJobVerdict
{
    [JsonStringEnumMemberName("ok")]
    Ok,

    [JsonStringEnumMemberName("late")]
    Late,

    [JsonStringEnumMemberName("failed")]
    Failed,

    [JsonStringEnumMemberName("retrying")]
    Retrying,

    [JsonStringEnumMemberName("missing")]
    Missing,

    [JsonStringEnumMemberName("disabled")]
    Disabled,
}

public sealed record DashboardJobsDto(bool RecurringEnabled, IReadOnlyList<DashboardJobDto> Jobs);

public sealed record DashboardJobDto(
    string Id,
    DashboardJobVerdict Verdict,
    int LateAfterMinutes,
    string? LastState,
    DateTimeOffset? LastExecution,
    DateTimeOffset? NextExecution,
    string Link);

public sealed record DashboardJobQueueDto(
    long Enqueued,
    long Processing,
    long Retrying,
    long Failed,
    int LiveServers,
    int HeartbeatWithinMinutes);

public sealed record DashboardEmailDto(int FailedInWindow, int Retrying, int WindowDays);

public sealed record DashboardOutboxDto(
    int Pending,
    int Stuck,
    int StuckAfterMinutes,
    int Failed,
    DateTimeOffset? OldestPendingAt);

public sealed record DashboardStuckScansDto(int Stuck, int StuckAfterMinutes);

public sealed record DashboardPurchaseOrderTransportDto(bool Configured, int FailedSends);

public sealed record DashboardObjectStorageDto(bool Reachable);
