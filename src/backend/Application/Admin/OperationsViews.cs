// The vocabulary for the operator's screens: the recurring jobs, the outbox, the finance-sync monitor, and
// the two read-only settings views.
//
//
// THE JOBS MONITOR
//
// Registered is false when this application expects a job and the scheduler does not have it. The overview's
// health figure already counts that fault; this carries it per row so it is obvious which job.
//
// The schedule is absent for a job that is not registered, because there is none to report.
//
// The last state is the scheduler's own word for it, such as succeeded or failed, and absent if it has never
// run. It is deliberately not mapped onto a set of our own: it is their vocabulary, and inventing a mapping
// would hide a state we had not thought of.
//
// Whether recurring work is enabled at all is reported once for the whole screen rather than per row,
// because when it is off no row's next run time means anything.
//
//
// THE OUTBOX
//
// One message with its payload, for an operator deciding whether to replay it.

namespace MotsSupplierPortal.Application.Admin;

public sealed record RecurringJobRowDto(
    string Id,
    bool Registered,
    string? Cron,
    DateTimeOffset? LastExecution,
    DateTimeOffset? NextExecution,
    string? LastState);

public sealed record JobsMonitorDto(bool RecurringEnabled, IReadOnlyList<RecurringJobRowDto> Jobs);

public sealed record OutboxMessageRowDto(
    Guid Id,
    string Type,
    string SyncStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    string PayloadJson);

public sealed record OutboxMonitorDto(
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<OutboxMessageRowDto> Messages);

public sealed record ErpSyncRowDto(
    string RfqReferenceCode,
    string ErpSyncStatus,
    int ErpRetryCount,
    DateTimeOffset? ErpSyncedAt,
    string? ExternalPurchaseOrderRef);

public sealed record ErpSyncMonitorDto(
    bool TransportConfigured,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<ErpSyncRowDto> Awards);

public sealed record SecurityPostureDto(
    PasswordPolicyDto Password,
    LockoutPolicyDto Lockout,
    SessionPolicyDto Session,
    IReadOnlyList<string> MfaRequiredRoles,
    IReadOnlyList<RateLimitPolicyDto> RateLimits,
    string RegistrationMode);

public sealed record PasswordPolicyDto(
    int MinimumLength,
    bool RequireDigit,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireNonAlphanumeric);

public sealed record LockoutPolicyDto(int MaxFailedAttempts, int LockoutMinutes);

public sealed record SessionPolicyDto(int AccessTokenMinutes, int RefreshTokenDays, int ClockSkewSeconds);

public sealed record RateLimitPolicyDto(string Policy, int PermitLimit, int WindowSeconds);

public sealed record StorageSettingsDto(
    long MaxUploadBytes,
    IReadOnlyDictionary<string, string> AllowedTypes,
    string Bucket,
    bool ObjectStorageReachable,
    bool VirusScannerReachable,
    int DocumentCount,
    int PendingScanCount);
