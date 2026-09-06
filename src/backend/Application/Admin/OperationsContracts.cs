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

/// <summary>
/// SCR-726. The security policy this deployment is actually running, as an auditor would ask for it.
///
/// <para><b>Read-only, and that is the design rather than an unfinished half.</b> Every value here is
/// deployment configuration - a password floor, a lockout threshold, which roles must carry a second
/// factor. Moving them onto a screen would move a security decision from a reviewed deployment to a
/// runtime click, and the first thing an attacker with an admin session would do is lower the floor.
/// So the screen reports and does not edit, and says so.</para>
///
/// <para>Nothing secret is carried: no signing key, no connection string, no issuer secret. Policy
/// numbers only.</para>
/// </summary>
public sealed record SecurityPostureDto(
    PasswordPolicyDto Password,
    LockoutPolicyDto Lockout,
    SessionPolicyDto Session,
    /// <summary>The roles for which a second factor is mandatory at sign-in.</summary>
    IReadOnlyList<string> MfaRequiredRoles,
    IReadOnlyList<RateLimitPolicyDto> RateLimits,
    /// <summary>registration.mode, the one security-relevant value that IS administrator-owned - and it
    /// already has a screen (SCR-724), so this reports it and points there rather than editing it twice.</summary>
    string RegistrationMode);

/// <param name="RequireNonAlphanumeric">False by design. SECURITY-ARCHITECTURE.md §1.4 follows NIST
/// 800-63B: length over composition, so a passphrase is not punished in favour of "Password1!". Reported
/// so a reader can see it is a decision rather than an omission.</param>
public sealed record PasswordPolicyDto(
    int MinimumLength,
    bool RequireDigit,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireNonAlphanumeric);

public sealed record LockoutPolicyDto(int MaxFailedAttempts, int LockoutMinutes);

public sealed record SessionPolicyDto(int AccessTokenMinutes, int RefreshTokenDays, int ClockSkewSeconds);

public sealed record RateLimitPolicyDto(string Policy, int PermitLimit, int WindowSeconds);

public interface IGetSecurityPostureHandler
{
    Task<SecurityPostureDto> HandleAsync(CancellationToken ct);
}

/// <summary>
/// SCR-725. What this deployment does with uploaded files, and whether the things that store and scan
/// them are actually reachable.
///
/// <para><b>Read-only, for the same reason SCR-726 is.</b> The upload cap and the allowed types are a
/// SECURITY control - §4.1's allow-list exists because a client's Content-Type is not evidence - and
/// putting either behind an admin click would let a compromised admin session widen what the portal
/// accepts. Per-document-type rules that ARE administrator-owned (which documents are required, how long
/// they are valid) already have SCR-710's reference-data screen.</para>
/// </summary>
public sealed record StorageSettingsDto(
    long MaxUploadBytes,
    /// <summary>Extension to the content type its magic bytes must match. Both halves shown, because the
    /// pairing IS the rule: a .pdf whose bytes are a PNG is refused.</summary>
    IReadOnlyDictionary<string, string> AllowedTypes,
    string Bucket,
    /// <summary>Whether the object store answered just now. Not a cached health snapshot - an operator
    /// opening this screen is asking about now.</summary>
    bool ObjectStorageReachable,
    bool VirusScannerReachable,
    /// <summary>How many documents are stored and how many are waiting to be scanned. A backlog that never
    /// drains is the failure this screen exists to make visible.</summary>
    int DocumentCount,
    int PendingScanCount);

public interface IGetStorageSettingsHandler
{
    Task<StorageSettingsDto> HandleAsync(CancellationToken ct);
}
