// The ERP section of the administrator's dashboard, hidden from a viewer without admin.integrations.manage: the
// connection, the hourly sync and the supplier push. DashboardErpDto says what each figure means; this file says how it
// is read.
//
// IT NEVER CONTACTS THE ERP. It reads the connection's row, the audit trail, the supplier table and the deployment's Erp
// settings, and it takes no ERP client, no connection provider and no HTTP client in its constructor, so it could not
// call the ERP if it tried. ErpConnectionProvider is not used either, because it reads every column of the row and
// decrypts the stored secret, and the dashboard needs neither. The precedence rule it applies, an address saved on the
// row over the settings, is repeated in ConnectionAsync below, and the tests compare the two answers.
//
// THREE READS, EACH OF ONLY THE COLUMNS IT NEEDS. The connection comes first, from the columns the table was created
// with. The sync then reads the columns NightlyErpSync (#230) added, and the push the ones ErpSupplierPush (#232) added,
// each in its own query and inside its own catch, so an environment where a migration has not been applied by hand
// loses that part and keeps the rest. A failed part is logged here with its name, like a failed section in the frame,
// and the answer carries no text from the exception. Cancellation of the request is let through, as the frame lets it
// through.
//
// THE QUERIES RUN ONE AFTER ANOTHER on the section's own database context, which serves one query at a time. There are
// at most ten, all on the connection's single row, the audit table's index on action and time, or the supplier table.
// The push's stalled queries are not run at all while the switch is off.
//
// THE CLOSING ROW IS FOUND FROM THE CONNECTION'S TIME. The import records the outcome on the connection and adds the
// closing row in one save, the row a moment after, so the closing row of the run the connection records is the first one
// at or after that time. Taking the latest closing row instead could put one run's counts beside another run's outcome.
//
// AN IMPORT IS UNFINISHED when it started more than thirty minutes ago, after the last outcome the connection records,
// and has no closing row of its own on the trail, matched by its correlation and dated at or after its start. Each half
// covers what the other cannot. The closing row is the record that this run ended. The connection's time settles every
// run before it, including those from before the trail recorded endings, which have no closing row and never will, and
// an import that died weeks ago and was followed by runs that finished.
//
// IT READS EVERY SUPPLIER, NOT THE CALLER'S. The push serves the deployment, not one buying body, and the section is a
// system administrator's, gated by permission, as the integrations screen's waiting count is; RowScopeGuardTests lists
// it among the exemptions for that reason.

namespace MotsSupplierPortal.Infrastructure.Admin.Dashboard;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Integration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ErpSectionHandler(
    AppDbContext db,
    IOptions<ErpOptions> settings,
    ILogger<ErpSectionHandler> logger)
    : IDashboardSectionHandler<DashboardErpDto>
{
    // The thresholds the owner confirmed: the hourly sync is stale after three hours, an import that has recorded no
    // outcome needs attention after thirty minutes, and a push is stalled when its next attempt is fifteen minutes
    // overdue or its in-flight marker ten minutes old.
    public static readonly TimeSpan SyncStaleAfter = TimeSpan.FromHours(3);
    public static readonly TimeSpan ImportUnfinishedAfter = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan PushOverdueAfter = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan PushInFlightAfter = TimeSpan.FromMinutes(10);
    public const int ReferenceCodesShown = 5;

    private static readonly string[] ClosingActions =
        [SupplierAuditActions.ErpImportCompleted, SupplierAuditActions.ErpImportFailed];

    private sealed record Connection(DashboardErpConnectionDto View, string? Address);

    public async Task<DashboardErpDto> RunAsync(DashboardRequest request, CancellationToken ct)
    {
        var connection = await ConnectionAsync(ct);
        var sync = await PartAsync("sync", () => SyncAsync(connection.View, request.AsOf, ct), ct);
        var push = await PartAsync("push", () => PushAsync(connection.Address, request.AsOf, ct), ct);

        return new DashboardErpDto(connection.View, sync, push);
    }

    private async Task<DashboardSection<TData>> PartAsync<TData>(
        string name, Func<Task<TData>> read, CancellationToken ct)
        where TData : class
    {
        try
        {
            return DashboardSection<TData>.Ok(await read());
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "The admin dashboard's ERP {Part} could not be read.", name);
            return DashboardSection<TData>.Failed();
        }
    }

    // The address in force: the row's once somebody has saved one, the settings' until then, as ErpConnectionProvider
    // decides it. A blank address on the row means "not configured here". An address that is not an absolute http or
    // https URL has no host to show, though it is still the one in force.
    private async Task<Connection> ConnectionAsync(CancellationToken ct)
    {
        var row = await db.IntegrationConnections
            .AsNoTracking()
            .Where(c => c.Key == IntegrationConnection.ErpKey)
            .Select(c => new { c.BaseUrl, c.IsEnabled, c.LastTestedAt, c.LastTestSucceeded })
            .FirstOrDefaultAsync(ct);

        var erp = settings.Value;
        var (address, enabled, source) =
            !string.IsNullOrWhiteSpace(row?.BaseUrl) ? (row.BaseUrl, row.IsEnabled, DashboardErpSource.Database)
            : !string.IsNullOrWhiteSpace(erp.BaseUrl) ? (erp.BaseUrl, erp.Enabled, DashboardErpSource.Configuration)
            : ((string?)null, false, DashboardErpSource.None);

        var server = address is not null
            && Uri.TryCreate(address, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
                ? parsed
                : null;

        return new Connection(
            new DashboardErpConnectionDto(
                source,
                enabled,
                server?.Authority,
                server is null ? null : server.Scheme == Uri.UriSchemeHttps,
                row?.LastTestedAt,
                row?.LastTestSucceeded),
            address);
    }

    private async Task<DashboardErpSyncDto> SyncAsync(
        DashboardErpConnectionDto connection, DateTimeOffset asOf, CancellationToken ct)
    {
        var last = await db.IntegrationConnections
            .AsNoTracking()
            .Where(c => c.Key == IntegrationConnection.ErpKey)
            .Select(c => new { c.LastSyncAt, c.LastSyncOutcome })
            .FirstOrDefaultAsync(ct);

        var lastRunAt = last?.LastSyncAt;

        var closing = lastRunAt is null
            ? null
            : await db.AuditLogs
                .AsNoTracking()
                .Where(a => ClosingActions.Contains(a.Action) && a.OccurredAt >= lastRunAt)
                .OrderBy(a => a.OccurredAt)
                .ThenBy(a => a.Id)
                .Select(a => new { a.Action, a.Changes })
                .FirstOrDefaultAsync(ct);

        var startedBefore = asOf - ImportUnfinishedAfter;
        var unfinished = await db.AuditLogs
            .AsNoTracking()
            .Where(run => run.Action == SupplierAuditActions.ErpImportRun
                && run.OccurredAt <= startedBefore
                && (lastRunAt == null || run.OccurredAt > lastRunAt)
                && !db.AuditLogs.Any(end =>
                    ClosingActions.Contains(end.Action)
                    && end.CorrelationId == run.CorrelationId
                    && end.OccurredAt >= run.OccurredAt))
            .OrderBy(run => run.OccurredAt)
            .Select(run => (DateTimeOffset?)run.OccurredAt)
            .FirstOrDefaultAsync(ct);

        var details = Details(closing?.Changes);

        return new DashboardErpSyncDto(
            lastRunAt,
            last?.LastSyncOutcome,
            Trigger(details),
            closing?.Action == SupplierAuditActions.ErpImportCompleted ? Counts(details) : null,
            connection.Enabled && (lastRunAt is null || lastRunAt < asOf - SyncStaleAfter),
            unfinished);
    }

    private async Task<DashboardErpPushDto> PushAsync(string? address, DateTimeOffset asOf, CancellationToken ct)
    {
        var writes = await db.IntegrationConnections
            .AsNoTracking()
            .Where(c => c.Key == IntegrationConnection.ErpKey)
            .Select(c => new { c.CreateSuppliersInErp, c.DefaultSupplierGroup })
            .FirstOrDefaultAsync(ct);

        var switchOn = writes?.CreateSuppliersInErp ?? false;

        var waiting = await db.Suppliers.CountAsync(SupplierErpPushJob.Pushable, ct);

        var failed = db.Suppliers.Where(s => s.ErpPushStatus == SupplierErpPushStatus.Failed);
        var failedCount = await failed.CountAsync(ct);
        var failedCodes = await FirstCodesAsync(failed, ct);

        int? stalledCount = null;
        IReadOnlyList<string> stalledCodes = [];

        if (switchOn)
        {
            var dueBefore = asOf - PushOverdueAfter;
            var startedBefore = asOf - PushInFlightAfter;
            var stalled = db.Suppliers
                .Where(SupplierErpPushJob.Pushable)
                .Where(s => s.ErpPushNextAttemptAt < dueBefore || s.ErpPushStartedAt < startedBefore);

            stalledCount = await stalled.CountAsync(ct);
            stalledCodes = await FirstCodesAsync(stalled, ct);
        }

        return new DashboardErpPushDto(
            switchOn,
            writes?.DefaultSupplierGroup,
            address is not null && ErpWriteHosts.Lists(settings.Value.WriteHosts, address),
            waiting,
            failedCount,
            stalledCount,
            failedCodes,
            stalledCodes);
    }

    private static async Task<IReadOnlyList<string>> FirstCodesAsync(IQueryable<Supplier> suppliers, CancellationToken ct) =>
        await suppliers
            .OrderBy(s => s.ErpPushRequestedAt)
            .ThenBy(s => s.Id)
            .Select(s => s.ReferenceCode)
            .Take(ReferenceCodesShown)
            .ToListAsync(ct);

    // The closing row's details, as RunErpImportHandler writes them: the trigger and, for a run that finished, the six
    // counts. A row whose details cannot be read gives no trigger and no counts rather than failing the sync.
    private static JsonElement? Details(string? changes)
    {
        if (changes is null) return null;

        try
        {
            using var document = JsonDocument.Parse(changes);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ErpImportTrigger? Trigger(JsonElement? details) =>
        details is { } run
        && run.TryGetProperty("trigger", out var trigger)
        && trigger.ValueKind == JsonValueKind.String
        && Enum.TryParse<ErpImportTrigger>(trigger.GetString(), ignoreCase: false, out var how)
        && Enum.IsDefined(how)
            ? how
            : null;

    private static DashboardErpSyncCounts? Counts(JsonElement? details)
    {
        if (details is not { } run) return null;

        int? Count(string name) =>
            run.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count)
                ? count
                : null;

        return (Count("erpSuppliers"), Count("created"), Count("updated"), Count("suspended"), Count("refused"), Count("failed"))
            is (int erpSuppliers, int created, int updated, int suspended, int refused, int failedCount)
            ? new DashboardErpSyncCounts(erpSuppliers, created, updated, suspended, refused, failedCount)
            : null;
    }
}
