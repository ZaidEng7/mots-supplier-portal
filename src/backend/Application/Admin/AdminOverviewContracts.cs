// The vocabulary for the system administrator's overview.
//
// Read-only, and every figure is something already in the database. The point of the screen is to surface
// state that exists and is otherwise visible only by querying the database by hand.
//
// The reference-data figures are health rather than a listing: per table, how many codes are active and how
// many are switched off. A table with no active codes is a configuration fault that blocks registration, and
// it was invisible.
//
// The audit figure is a count of rows written in the last day rather than a listing. The overview is gated on
// admin.users.manage alone, and a listing there would show audit rows to an administrator the audit search
// would refuse. The administrator's dashboard that replaces it does list rows, the latest sensitive changes and
// the recent activity feed, and hides both from a viewer without audit.read, the permission the audit search
// itself is gated on.

namespace MotsSupplierPortal.Application.Admin;

public sealed record AdminCountDto(string Key, int Count);

public sealed record AdminOverviewDto(
    IReadOnlyList<AdminCountDto> UsersByRole,
    int TotalRoles,
    IReadOnlyList<ReferenceTableHealthDto> ReferenceData,
    OutboxHealthDto Outbox,
    JobHealthDto Jobs,
    int AuditRowsLast24Hours);

public sealed record ReferenceTableHealthDto(string Table, int Active, int Inactive);

public sealed record OutboxHealthDto(
    int Pending,
    int Failed,
    int? OldestPendingAgeMinutes,
    bool ErpTransportConfigured);

public sealed record JobHealthDto(
    bool RecurringJobsEnabled,
    IReadOnlyList<string> ExpectedJobs,
    IReadOnlyList<string> RegisteredJobs,
    IReadOnlyList<string> MissingJobs);

public interface IGetAdminOverviewHandler
{
    Task<AdminOverviewDto> HandleAsync(CancellationToken ct);
}
