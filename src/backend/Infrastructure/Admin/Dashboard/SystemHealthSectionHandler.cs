// The system health section of the administrator's dashboard: whether the recurring jobs are running, the job
// queue, mail, the outbox, stuck scans, pending migrations, the reference lists, the purchase-order transport and
// the object store. What each figure means is written beside its record in DashboardSystemHealthDto.cs.
//
// It is resolved from a scope of its own and runs beside the other sections, as GetAdminDashboardHandler
// describes, so it uses its own database context freely, and the job scheduler's monitoring calls, which have no
// asynchronous form, do not hold anybody else up.
//
//
// IT READS WHAT THE OPERATIONS PAGE ALREADY READS
//
// The job rows come from the jobs monitor, the purchase-order transport and its failed sends from the
// purchase-order send monitor, the outbox and the reference lists from the reads the overview uses, and the object
// store from its readiness check. This section adds the judgement on top: which job is late, what counts as
// stuck, which scheduled jobs are retries. A second copy of any of those reads could drift from the screen an
// administrator opens next to check it.
//
// Every window is measured from the one instant the dashboard was asked at, never from the clock at the moment a
// query happens to run.
//
//
// THE THRESHOLDS ARE THE ONES THE OWNER CONFIRMED
//
// Each job's schedule is written here with the grace it is allowed beyond it: 10 minutes for the five-minute jobs,
// which makes them late after 15, and 2 hours for the hourly and daily ones, which makes them late after 3 and 26
// hours. The supplier push runs every five minutes and the supplier import every hour, so they are judged like
// the rest of their kind.
//
// A job this application schedules without a line here fails the section rather than being judged against a
// guess. The section's test keeps its own list of the eight, so a ninth fails there first.
//
// Outbox messages and supplier documents are stuck after 15 minutes. The threshold is public so that anything
// acting on stuck documents uses the same line as the figure that reports them.
//
//
// WHAT IT WILL NOT DO
//
// It never contacts the ERP. The only call outside the database and the scheduler's storage is the object-store
// ping, which gives up after five seconds and answers unreachable rather than holding the dashboard. It starts
// first so it runs while the database is being read.
//
// It reads every supplier's documents, to count the stuck ones, and is listed with its reason in the row-scope
// guard's exemptions: the dashboard is the platform administrator's, gated on admin.users.manage, and a stuck scan
// is a fault of the deployment rather than of one supplier. Only a count leaves this class.

namespace MotsSupplierPortal.Infrastructure.Admin.Dashboard;

using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SystemHealthSectionHandler(
    AppDbContext db,
    IGetJobsMonitorHandler jobsMonitor,
    IGetErpSyncMonitorHandler purchaseOrderSends,
    JobStorage jobStorage,
    HealthCheckService healthChecks)
    : IDashboardSectionHandler<DashboardSystemHealthDto>
{
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan ObjectStoragePingLimit = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan HeartbeatWithin = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan EmailWindow = TimeSpan.FromDays(7);

    private const string OperationsPage = "/back-office/operations";

    private const int PageSize = 500;

    private static readonly JobSchedule EveryFiveMinutes = new(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));

    private static readonly JobSchedule Hourly = new(TimeSpan.FromHours(1), TimeSpan.FromHours(2));

    private static readonly JobSchedule Daily = new(TimeSpan.FromDays(1), TimeSpan.FromHours(2));

    private static readonly IReadOnlyDictionary<string, JobSchedule> Schedules =
        new Dictionary<string, JobSchedule>(StringComparer.Ordinal)
        {
            ["document-expiry-lifecycle"] = Daily,
            ["draft-registration-cleanup"] = Daily,
            ["outbox-dispatch"] = EveryFiveMinutes,
            ["rfq-timeline"] = EveryFiveMinutes,
            ["award-erp-sync"] = EveryFiveMinutes,
            ["idempotency-cleanup"] = Hourly,
            [ErpSupplierSyncJob.JobId] = Hourly,
            [SupplierErpPushJob.JobId] = EveryFiveMinutes,
        };

    public async Task<DashboardSystemHealthDto> RunAsync(DashboardRequest request, CancellationToken ct)
    {
        var asOf = request.AsOf;
        var stuckBefore = asOf - StuckAfter;

        var objectStorage = DependencyProbes.ObjectStorageAnswersAsync(healthChecks, ObjectStoragePingLimit, ct);

        var jobs = Jobs(request);
        var (queue, email) = SchedulerFigures(asOf);

        var outbox = await OperationalHealthReads.OutboxAsync(db, ct);
        var stuckMessages = await db.OutboxMessages.AsNoTracking().CountAsync(
            m => m.SyncStatus == OutboxSyncStatus.Pending && m.CreatedAt < stuckBefore, ct);

        var stuckScans = await db.SupplierDocuments.AsNoTracking().CountAsync(
            d => d.State == DocumentState.PendingScan && d.UploadedAt < stuckBefore, ct);

        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        var referenceLists = await OperationalHealthReads.ReferenceTablesAsync(db, ct);

        var sends = await purchaseOrderSends.HandleAsync(nameof(ErpSyncStatus.Failed), ct);

        return new DashboardSystemHealthDto(
            jobs,
            queue,
            email,
            new DashboardOutboxDto(
                outbox.Pending, stuckMessages, (int)StuckAfter.TotalMinutes, outbox.Failed, outbox.OldestPendingAt),
            new DashboardStuckScansDto(stuckScans, (int)StuckAfter.TotalMinutes),
            pendingMigrations,
            referenceLists,
            new DashboardPurchaseOrderTransportDto(
                sends.TransportConfigured, sends.Counts[nameof(ErpSyncStatus.Failed)]),
            new DashboardObjectStorageDto(await objectStorage));
    }

    private DashboardJobsDto Jobs(DashboardRequest request)
    {
        var monitor = jobsMonitor.Handle();
        var rows = monitor.Jobs.ToDictionary(row => row.Id, StringComparer.Ordinal);

        var jobs = RecurringJobs.All
            .Select(id =>
            {
                if (!Schedules.TryGetValue(id, out var schedule))
                {
                    throw new InvalidOperationException(
                        $"The recurring job {id} has no schedule on the dashboard, so it cannot be judged late.");
                }

                rows.TryGetValue(id, out var row);

                return new DashboardJobDto(
                    id,
                    Verdict(monitor.RecurringEnabled, row, schedule, request.AsOf),
                    (int)schedule.LateAfter.TotalMinutes,
                    row?.LastState,
                    row?.LastExecution,
                    row?.NextExecution,
                    LinkFor(id, request.Viewer));
            })
            .ToList();

        return new DashboardJobsDto(monitor.RecurringEnabled, jobs);
    }

    private static DashboardJobVerdict Verdict(
        bool recurringEnabled, RecurringJobRowDto? row, JobSchedule schedule, DateTimeOffset asOf)
    {
        if (!recurringEnabled) return DashboardJobVerdict.Disabled;
        if (row is null || !row.Registered) return DashboardJobVerdict.Missing;

        var late = row.LastExecution is { } last
            ? asOf - last > schedule.LateAfter
            : row.NextExecution is not { } next || asOf - next > schedule.Grace;

        if (late) return DashboardJobVerdict.Late;

        return row.LastState switch
        {
            FailedState.StateName => DashboardJobVerdict.Failed,
            ScheduledState.StateName => DashboardJobVerdict.Retrying,
            _ => DashboardJobVerdict.Ok,
        };
    }

    private static string LinkFor(string jobId, DashboardViewer viewer) =>
        jobId == ErpSupplierSyncJob.JobId && viewer.HasPermission(Permissions.SupplierImportRun)
            ? RecurringJobs.StartedFromTheirOwnScreen[jobId]
            : OperationsPage;

    private (DashboardJobQueueDto Queue, DashboardEmailDto Email) SchedulerFigures(DateTimeOffset asOf)
    {
        var monitoring = jobStorage.GetMonitoringApi();

        var statistics = monitoring.GetStatistics();
        var scheduled = Every<ScheduledJobDto>(monitoring.ScheduledJobs);
        var failed = Every<FailedJobDto>(monitoring.FailedJobs);
        var servers = monitoring.Servers();

        var importSecondTries = scheduled.Count(job => IsImportSecondTry(job.Value.Job));
        var liveServers = servers.Count(server =>
            server.Heartbeat is { } heartbeat && asOf - Utc(heartbeat) <= HeartbeatWithin);

        var emailsFailed = failed.Count(job =>
            IsEmail(job.Value.Job) && job.Value.FailedAt is { } failedAt && Utc(failedAt) >= asOf - EmailWindow);
        var emailsRetrying = scheduled.Count(job => IsEmail(job.Value.Job));

        return (
            new DashboardJobQueueDto(
                statistics.Enqueued,
                statistics.Processing,
                scheduled.Count - importSecondTries,
                statistics.Failed,
                liveServers,
                (int)HeartbeatWithin.TotalMinutes),
            new DashboardEmailDto(emailsFailed, emailsRetrying, (int)EmailWindow.TotalDays));
    }

    private static List<KeyValuePair<string, TJob>> Every<TJob>(Func<int, int, JobList<TJob>> page)
    {
        var every = new List<KeyValuePair<string, TJob>>();

        for (var from = 0; ; from += PageSize)
        {
            var batch = page(from, PageSize);
            every.AddRange(batch);

            if (batch.Count < PageSize) return every;
        }
    }

    private static bool IsImportSecondTry(Job? job) =>
        job?.Type == typeof(ErpSupplierSyncJob) && job.Method.Name == nameof(ErpSupplierSyncJob.RunAgainAsync);

    private static bool IsEmail(Job? job) => job?.Type == typeof(EmailJobs);

    private static DateTimeOffset Utc(DateTime value) =>
        new(value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record JobSchedule(TimeSpan Every, TimeSpan Grace)
    {
        public TimeSpan LateAfter => Every + Grace;
    }
}
