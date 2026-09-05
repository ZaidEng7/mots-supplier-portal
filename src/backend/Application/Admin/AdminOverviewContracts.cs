namespace MotsSupplierPortal.Application.Admin;

public sealed record AdminCountDto(string Key, int Count);

/// <summary>
/// T-062/FR-DSH-006/SCR-700: <i>"Admin dashboard: users/roles, reference-data health,
/// integration/outbox status, job health, audit access."</i>
///
/// <para><b>Read-only, and every figure is something already in the database.</b> Nothing here is a
/// new metric or a new store - the point of an admin dashboard is to surface state that exists and is
/// currently only visible by querying Postgres by hand.</para>
/// </summary>
public sealed record AdminOverviewDto(
    IReadOnlyList<AdminCountDto> UsersByRole,
    int TotalRoles,
    /// <summary>Per reference table, how many codes are active and how many are deactivated. Health
    /// rather than a listing: a table with zero active codes is a configuration fault that blocks
    /// registration, and it is invisible today.</summary>
    IReadOnlyList<ReferenceTableHealthDto> ReferenceData,
    OutboxHealthDto Outbox,
    JobHealthDto Jobs,
    /// <summary>Audit rows written in the last 24 hours. A count, not a listing - MSP-62 removed
    /// audit.read from ministry_viewer precisely because raw rows expose named actors and reviewer
    /// free text, and a dashboard tile has no business carrying either.</summary>
    int AuditRowsLast24Hours);

public sealed record ReferenceTableHealthDto(string Table, int Active, int Inactive);

public sealed record OutboxHealthDto(
    int Pending,
    int Failed,
    /// <summary>The age of the OLDEST pending message, in minutes. A backlog count alone does not say
    /// whether the dispatcher is running - ten messages queued a minute ago is normal, ten queued
    /// yesterday means it has stopped.</summary>
    int? OldestPendingAgeMinutes);

public sealed record JobHealthDto(
    /// <summary>False when <c>Jobs:EnableRecurring</c> is off. That setting silently disables every
    /// scheduled transition - submission windows never open, expiry is never flagged, the outbox is
    /// never drained - and today it is visible only as one warning line in the startup log.</summary>
    bool RecurringJobsEnabled,
    IReadOnlyList<string> ExpectedJobs,
    IReadOnlyList<string> RegisteredJobs,
    /// <summary>Expected but not registered. Non-empty is an operational fault, not a curiosity.</summary>
    IReadOnlyList<string> MissingJobs);

public interface IGetAdminOverviewHandler
{
    Task<AdminOverviewDto> HandleAsync(CancellationToken ct);
}
