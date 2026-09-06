namespace MotsSupplierPortal.Application.Admin;

/// <summary>
/// SCR-721. One recurring job as an operator needs to see it.
/// </summary>
/// <param name="Registered">False when this application expects the job and Hangfire does not have it -
/// the fault the admin overview's health tile already counts, carried per row so it is obvious WHICH.</param>
/// <param name="Cron">Null for a job that is not registered; there is no schedule to report.</param>
/// <param name="LastState">Hangfire's own last-run state ("Succeeded", "Failed", ...), or null if it has
/// never run. Not normalised into an enum of ours: it is Hangfire's vocabulary and inventing a mapping
/// would hide a state we had not thought of.</param>
public sealed record RecurringJobRowDto(
    string Id,
    bool Registered,
    string? Cron,
    DateTimeOffset? LastExecution,
    DateTimeOffset? NextExecution,
    string? LastState);

/// <param name="RecurringEnabled">Jobs:EnableRecurring. When false every schedule is off and no row's
/// NextExecution means anything, so the screen has to say it once rather than per row.</param>
public sealed record JobsMonitorDto(bool RecurringEnabled, IReadOnlyList<RecurringJobRowDto> Jobs);

/// <summary>SCR-722. One outbox message, with its payload, for an operator deciding whether to replay it.</summary>
public sealed record OutboxMessageRowDto(
    Guid Id,
    string Type,
    string SyncStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    string PayloadJson);

/// <param name="Counts">Every status with its count, including the zeroes: an operator reading "Failed"
/// absent from a list cannot tell it from a page that failed to load.</param>
public sealed record OutboxMonitorDto(
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<OutboxMessageRowDto> Messages);

public interface IGetJobsMonitorHandler
{
    JobsMonitorDto Handle();
}

public interface ITriggerRecurringJobHandler
{
    /// <summary>False when the job is not registered - triggering something Hangfire does not have would
    /// report success for nothing happening.</summary>
    bool Handle(string jobId);
}

public interface IGetOutboxMonitorHandler
{
    Task<OutboxMonitorDto> HandleAsync(string? status, CancellationToken ct);
}

public interface IReplayOutboxMessageHandler
{
    Task<bool> HandleAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// SCR-723. One award's ERP synchronisation, as an operator needs to judge it.
///
/// <para>Keyed by the RFQ's reference code, because an Award has no code of its own - checked in the
/// aggregate rather than assumed, and it is also what `POST /awards/{referenceCode}/retry-erp-sync`
/// takes, so the row carries exactly the identifier the retry needs.</para>
/// </summary>
/// <param name="ExternalPurchaseOrderRef">The ERP's own reference once it acknowledges. Null while the
/// sync has not succeeded - and null is the whole point of showing it: a Synced row with no reference
/// would mean the adapter reported success without returning anything.</param>
public sealed record ErpSyncRowDto(
    string RfqReferenceCode,
    string ErpSyncStatus,
    int ErpRetryCount,
    DateTimeOffset? ErpSyncedAt,
    string? ExternalPurchaseOrderRef);

/// <param name="TransportConfigured">BRULE-011: false when the logging stand-in is registered rather than
/// a real ERP transport. Without it every row on this screen is the stub talking to itself, and a wall of
/// Synced would read as a working integration while nothing has left the building.</param>
public sealed record ErpSyncMonitorDto(
    bool TransportConfigured,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<ErpSyncRowDto> Awards);

public interface IGetErpSyncMonitorHandler
{
    Task<ErpSyncMonitorDto> HandleAsync(string? status, CancellationToken ct);
}
