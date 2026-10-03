// The ERP section of the administrator's dashboard, hidden from a viewer without admin.integrations.manage: the
// connection, the hourly sync and the supplier push. DashboardErpDto says what each figure means; this file says how it
// is read.
//
// IT NEVER CONTACTS THE ERP. It reads the connection's row, the audit trail, the supplier table and the deployment's Erp
// settings, and it takes no ERP client, no connection provider and no HTTP client in its constructor, so it could not
// call the ERP if it tried. ErpConnectionProvider is not used either, because it reads every column of the row and
// decrypts the stored secret, and the dashboard needs neither. The precedence rule it applies, an address saved on the
// row over the settings, is repeated in InForce below, and the tests compare the two answers.
//
// THREE PARTS, EACH READING ONLY THE COLUMNS IT NEEDS, IN QUERIES OF ITS OWN AND INSIDE A CATCH OF ITS OWN. The
// connection reads the columns the table was created with, the sync the ones NightlyErpSync (#230) added, and the push
// the ones ErpSupplierPush (#232) added, with the supplier columns its Pushable rule takes from both. Each part reads
// the address in force and its switch itself, rather than being handed them by the connection, so an environment where
// a migration has not been applied by hand loses the part that needs it and keeps the rest. A failed part is logged
// here with its name, like a failed section in the frame, and the answer carries no text from the exception.
// Cancellation of the request is let through, as the frame lets it through.
//
// THE QUERIES RUN ONE AFTER ANOTHER on the section's own database context, which serves one query at a time. There are
// at most ten, all on the connection's single row, the audit table's index on action and time, or the supplier table.
// The push's stalled queries are not run at all while the switch is off.
//
// THE CLOSING ROW IS FOUND FROM THE CONNECTION'S TIME. The import records the outcome on the connection and adds the
// closing row in one save, the row a moment after, so the closing row of the run the connection records is the first
// one at or after that time, and within a minute of it. Taking the latest closing row instead could put one run's counts
// beside another run's outcome.
//
// AN IMPORT IS UNFINISHED when it started more than thirty minutes ago, after the last run the sync reports, and has no
// closing row of its own on the trail, matched by its correlation and dated at or after its start. Each half covers what
// the other cannot. The closing row is the record that this run ended. The last run's time settles every run before it,
// including those from before the trail recorded endings, which have no closing row and never will, and an import that
// died weeks ago and was followed by runs that finished.
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

    // How long after the connection's time a closing row can still belong to the run the connection records. The two
    // are written in one save, so a minute is far more than the gap between them and far less than the hour between runs.
    private static readonly TimeSpan ClosingRowWithin = TimeSpan.FromMinutes(1);

    private static readonly string[] ClosingActions =
        [SupplierAuditActions.ErpImportCompleted, SupplierAuditActions.ErpImportFailed];

    public async Task<DashboardErpDto> RunAsync(DashboardRequest request, CancellationToken ct)
    {
        var connection = await PartAsync("connection", () => ConnectionAsync(ct), ct);
        var sync = await PartAsync("sync", () => SyncAsync(request.AsOf, ct), ct);
        var push = await PartAsync("push", () => PushAsync(request.AsOf, ct), ct);

        return new DashboardErpDto(connection, sync, push);
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

    private sealed record AddressInForce(string? Address, bool Enabled, DashboardErpSource Source);

    // The address in force and its switch: the row's once somebody has saved an address, the settings' until then, as
    // ErpConnectionProvider decides it. A blank address on the row means "not configured here".
    private AddressInForce InForce(string? rowBaseUrl, bool rowEnabled)
    {
        var erp = settings.Value;

        return !string.IsNullOrWhiteSpace(rowBaseUrl) ? new(rowBaseUrl, rowEnabled, DashboardErpSource.Database)
            : !string.IsNullOrWhiteSpace(erp.BaseUrl) ? new(erp.BaseUrl, erp.Enabled, DashboardErpSource.Configuration)
            : new(null, false, DashboardErpSource.None);
    }

    // An address that is not an absolute http or https URL has no host to show, though it is still the one in force.
    private async Task<DashboardErpConnectionDto> ConnectionAsync(CancellationToken ct)
    {
        var row = await db.IntegrationConnections
            .AsNoTracking()
            .Where(c => c.Key == IntegrationConnection.ErpKey)
            .Select(c => new { c.BaseUrl, c.IsEnabled, c.LastTestedAt, c.LastTestSucceeded })
            .FirstOrDefaultAsync(ct);

        var inForce = InForce(row?.BaseUrl, row?.IsEnabled ?? false);

        var server = inForce.Address is not null
            && Uri.TryCreate(inForce.Address, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
                ? parsed
                : null;

        return new DashboardErpConnectionDto(
            inForce.Source,
            inForce.Enabled,
            server?.Authority,
            server is null ? null : server.Scheme == Uri.UriSchemeHttps,
            row?.LastTestedAt,
            row?.LastTestSucceeded);
    }

    // The run the connection records, and its closing row; or, when the connection records none, the latest closing
    // row on the trail, which is then the only record of a run there is.
    private async Task<DashboardErpSyncDto> SyncAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        var row = await db.IntegrationConnections
            .AsNoTracking()
            .Where(c => c.Key == IntegrationConnection.ErpKey)
            .Select(c => new { c.BaseUrl, c.IsEnabled, c.LastSyncAt, c.LastSyncOutcome })
            .FirstOrDefaultAsync(ct);

        var enabled = InForce(row?.BaseUrl, row?.IsEnabled ?? false).Enabled;
        var lastRunAt = row?.LastSyncAt;
        var outcome = row?.LastSyncOutcome;

        var closings = db.AuditLogs.AsNoTracking().Where(a => ClosingActions.Contains(a.Action));

        var closingBy = lastRunAt + ClosingRowWithin;
        var closing = lastRunAt is { } recorded
            ? await closings
                .Where(a => a.OccurredAt >= recorded && a.OccurredAt < closingBy)
                .OrderBy(a => a.OccurredAt)
                .ThenBy(a => a.Id)
                .Select(a => new { a.Action, a.OccurredAt, a.ToState, a.Changes })
                .FirstOrDefaultAsync(ct)
            : await closings
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .Select(a => new { a.Action, a.OccurredAt, a.ToState, a.Changes })
                .FirstOrDefaultAsync(ct);

        if (lastRunAt is null && closing is not null)
        {
            lastRunAt = closing.OccurredAt;
            outcome = Enum.TryParse<IntegrationSyncOutcome>(closing.ToState, ignoreCase: false, out var parsed)
                && Enum.IsDefined(parsed)
                    ? parsed
                    : null;
        }

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
            outcome,
            Trigger(details),
            closing?.Action == SupplierAuditActions.ErpImportCompleted ? Counts(details) : null,
            enabled && (lastRunAt is null || lastRunAt < asOf - SyncStaleAfter),
            unfinished,
            enabled);
    }

    private async Task<DashboardErpPushDto> PushAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        var row = await db.IntegrationConnections
            .AsNoTracking()
            .Where(c => c.Key == IntegrationConnection.ErpKey)
            .Select(c => new { c.BaseUrl, c.IsEnabled, c.CreateSuppliersInErp, c.DefaultSupplierGroup })
            .FirstOrDefaultAsync(ct);

        var address = InForce(row?.BaseUrl, row?.IsEnabled ?? false).Address;
        var switchOn = row?.CreateSuppliersInErp ?? false;

        var waiting = await db.Suppliers.CountAsync(SupplierErpPushJob.Pushable, ct);

        var failed = db.Suppliers.Where(s => s.ErpPushStatus == SupplierErpPushStatus.Failed);
        var failedCount = await failed.CountAsync(ct);
        var attention = await FirstAsync(failed, ct);

        int? stalledCount = null;

        if (switchOn)
        {
            var dueBefore = asOf - PushOverdueAfter;
            var startedBefore = asOf - PushInFlightAfter;
            var stalled = db.Suppliers
                .Where(SupplierErpPushJob.Pushable)
                .Where(s => s.ErpPushNextAttemptAt < dueBefore || s.ErpPushStartedAt < startedBefore);

            stalledCount = await stalled.CountAsync(ct);
            attention = [.. attention, .. await FirstAsync(stalled, ct)];
        }

        return new DashboardErpPushDto(
            switchOn,
            row?.DefaultSupplierGroup,
            address is not null && ErpWriteHosts.Lists(settings.Value.WriteHosts, address),
            waiting,
            failedCount,
            stalledCount,
            attention
                .OrderBy(s => s.RequestedAt ?? DateTimeOffset.MaxValue)
                .ThenBy(s => s.Id)
                .Select(s => s.ReferenceCode)
                .Take(ReferenceCodesShown)
                .ToList());
    }

    private sealed record Flagged(Guid Id, string ReferenceCode, DateTimeOffset? RequestedAt);

    // The first few of a set, in the order the push takes them, oldest request first. The failed and the stalled sets
    // never share a supplier, since a failed push is not one the push works on.
    private static async Task<List<Flagged>> FirstAsync(IQueryable<Supplier> suppliers, CancellationToken ct) =>
        await suppliers
            .AsNoTracking()
            .OrderBy(s => s.ErpPushRequestedAt == null)
            .ThenBy(s => s.ErpPushRequestedAt)
            .ThenBy(s => s.Id)
            .Select(s => new Flagged(s.Id, s.ReferenceCode, s.ErpPushRequestedAt))
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
