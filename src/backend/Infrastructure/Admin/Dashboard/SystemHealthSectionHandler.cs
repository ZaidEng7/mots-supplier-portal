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
// administrator opens next to check it. The one exception is the mail figures and the import's second tries,
// which no screen shows: they are counted in the scheduler's own tables, in one query, as SchedulerCountsAsync
// explains, because the scheduler's monitoring calls could only page through every failed job ever kept.
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
// Outbox messages and supplier documents are stuck after 15 minutes. The documents' line is
// StuckScans.PendingLongerThan, the one the stuck-scan retry acts on, so the figure counts exactly the documents
// that pressing retry would take up. The outbox's line is StuckAfter, public for the same reason.
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
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
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
    HealthCheckService healthChecks,
    IConfiguration configuration)
    : IDashboardSectionHandler<DashboardSystemHealthDto>
{
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan ObjectStoragePingLimit = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan HeartbeatWithin = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan EmailWindow = TimeSpan.FromDays(7);

    private const string OperationsPage = "/back-office/operations";

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
        var (queue, email) = await SchedulerFiguresAsync(asOf, ct);

        var outbox = await OperationalHealthReads.OutboxAsync(db, ct);
        var stuckMessages = await db.OutboxMessages.AsNoTracking().CountAsync(
            m => m.SyncStatus == OutboxSyncStatus.Pending && m.CreatedAt < stuckBefore, ct);

        var scansStuckBefore = asOf - StuckScans.PendingLongerThan;
        var stuckScans = await db.SupplierDocuments.AsNoTracking().CountAsync(
            d => d.State == DocumentState.PendingScan && d.UploadedAt < scansStuckBefore, ct);

        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        var referenceLists = await OperationalHealthReads.ReferenceTablesAsync(db, ct);

        var sends = await purchaseOrderSends.HandleAsync(nameof(ErpSyncStatus.Failed), ct);

        return new DashboardSystemHealthDto(
            jobs,
            queue,
            email,
            new DashboardOutboxDto(
                outbox.Pending, stuckMessages, (int)StuckAfter.TotalMinutes, outbox.Failed, outbox.OldestPendingAt),
            new DashboardStuckScansDto(stuckScans, (int)StuckScans.PendingLongerThan.TotalMinutes),
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
        if (row.LastState == FailedState.StateName) return DashboardJobVerdict.Failed;
        if (row.LastState == ScheduledState.StateName) return DashboardJobVerdict.Retrying;

        return DashboardJobVerdict.Ok;
    }

    private static string LinkFor(string jobId, DashboardViewer viewer) =>
        jobId == ErpSupplierSyncJob.JobId && viewer.HasPermission(Permissions.SupplierImportRun)
            ? RecurringJobs.StartedFromTheirOwnScreen[jobId]
            : OperationsPage;

    // The scheduler's own statistics give the queue, and its server list the heartbeats. The three figures that
    // depend on which job a row is, failed and retrying mail and the import's second tries, are counted in one
    // query against the scheduler's tables instead, as SchedulerCountsAsync describes.
    private async Task<(DashboardJobQueueDto Queue, DashboardEmailDto Email)> SchedulerFiguresAsync(
        DateTimeOffset asOf, CancellationToken ct)
    {
        var monitoring = jobStorage.GetMonitoringApi();

        var statistics = monitoring.GetStatistics();
        var servers = monitoring.Servers();

        var liveServers = servers.Count(server =>
            server.Heartbeat is { } heartbeat && asOf - Utc(heartbeat) <= HeartbeatWithin);

        var counts = await SchedulerCountsAsync(asOf - EmailWindow, ct);

        return (
            new DashboardJobQueueDto(
                statistics.Enqueued,
                statistics.Processing,
                statistics.Scheduled - counts.ImportSecondTries,
                statistics.Failed,
                liveServers,
                (int)HeartbeatWithin.TotalMinutes),
            new DashboardEmailDto(counts.EmailsFailed, counts.EmailsRetrying, (int)EmailWindow.TotalDays));
    }

    // Failed jobs are never removed by the scheduler, because Failed is not a final state, so reading them through
    // the monitoring calls would page through every failure the deployment has ever had, with its full state data,
    // on each dashboard read; and that list is ordered by job id, so stopping early would not be correct either.
    // The counts are therefore one query in the database: the job's current state, the time that state was entered
    // (state.createdat, which for a failure is when it failed), and the class and method stored in the job's
    // invocation data. The class is compared without its assembly, as the part before the first comma, and both the
    // compact ("t", "m") and the older ("Type", "Method") field names are read, so a job written under either form
    // is counted.
    //
    // The scheduler's tables live in the schema the deployment configures under Hangfire:SchemaName, the same
    // setting PersistenceRegistration hands the scheduler, in the application's own database. The query text is a
    // constant: the schema reaches the database as a parameter to set_config, which points this transaction's
    // search_path at it, and the time and the names are parameters too. Nothing is spliced into the SQL, and the
    // setting ends with the transaction, so a pooled connection goes back with its usual search_path.
    private async Task<SchedulerCounts> SchedulerCountsAsync(DateTimeOffset failedSince, CancellationToken ct)
    {
        var schema = Quote(configuration.GetValue("Hangfire:SchemaName", defaultValue: "hangfire")!);

        const string sql = """
            SELECT
                COUNT(*) FILTER (WHERE j.statename = 'Failed' AND x.type = @email AND s.createdat >= @failedSince),
                COUNT(*) FILTER (WHERE j.statename = 'Scheduled' AND x.type = @email),
                COUNT(*) FILTER (WHERE j.statename = 'Scheduled' AND x.type = @import AND x.method = @secondTry)
            FROM "job" j
            LEFT JOIN "state" s ON s."id" = j."stateid"
            CROSS JOIN LATERAL (SELECT
                split_part(COALESCE(j."invocationdata" ->> 't', j."invocationdata" ->> 'Type'), ',', 1) AS type,
                COALESCE(j."invocationdata" ->> 'm', j."invocationdata" ->> 'Method') AS method) x
            WHERE j."statename" IN ('Failed', 'Scheduled')
            """;

        var connection = db.Database.GetDbConnection();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var dbTransaction = transaction.GetDbTransaction();

        await using (var searchPath = connection.CreateCommand())
        {
            searchPath.Transaction = dbTransaction;
            searchPath.CommandText = "SELECT set_config('search_path', @schema, true)";
            Add(searchPath, "schema", schema);
            await searchPath.ExecuteNonQueryAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbTransaction;
        command.CommandText = sql;
        Add(command, "email", typeof(EmailJobs).FullName!);
        Add(command, "import", typeof(ErpSupplierSyncJob).FullName!);
        Add(command, "secondTry", nameof(ErpSupplierSyncJob.RunAgainAsync));
        Add(command, "failedSince", failedSince.ToUniversalTime());

        SchedulerCounts counts;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            counts = new SchedulerCounts(
                (int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2));
        }

        await transaction.CommitAsync(ct);
        return counts;
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static DateTimeOffset Utc(DateTime value) =>
        new(value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record SchedulerCounts(int EmailsFailed, int EmailsRetrying, int ImportSecondTries);

    private sealed record JobSchedule(TimeSpan Every, TimeSpan Grace)
    {
        public TimeSpan LateAfter => Every + Grace;
    }
}
