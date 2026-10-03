// The vocabulary for the system administrator's dashboard: one answer made of six sections, each of which is
// ok, failed or hidden on its own.
//
// It replaced the administrator's overview, whose answer was all or nothing: one query that threw there failed the
// whole screen, at the moment an administrator most needed the rest of it.
//
//
// A SECTION IS OK, FAILED OR HIDDEN, AND ONLY OK CARRIES DATA
//
// Hidden means the viewer does not hold the permission that section needs. The section never ran, so there is
// nothing to show and nothing to leak.
//
// Failed means the section ran and threw. The exception is logged on the server and goes no further: a failed
// section carries no message, no type name and no partial figures, because an exception's text can name a
// table, a host or a column, and this answer goes to a browser.
//
// The data is absent rather than empty in both cases, so a screen cannot draw a zero that was never counted.
//
// The status goes over the wire in lower case, ok, failed and hidden, which is the vocabulary the screen and the
// specification use.
//
//
// WHO IS ASKING IS READ ONCE, BEFORE ANY SECTION STARTS
//
// The viewer is the caller's id and the set of permissions their token carries, copied out of the request before
// the sections start in parallel. A section reads the copy and never the request. The sections run in scopes of
// their own on other threads, and one fact read once cannot disagree with itself across six of them.
//
// The moment of asking travels with it. Every window on the dashboard, the last 24 hours or the last 7 days, is
// measured from that one instant, so two sections counting the same day count the same day.
//
//
// SIX SECTIONS, AND THE LAST ONE READS THE OTHER FIVE
//
// Each section is its own handler with its own data record, in a file of its own, so the sections can be built
// and changed without touching one another.
//
// Needs attention is computed from the other five sections' results. It runs after them and is handed all five,
// hidden and failed ones included, because "this check could not run" is itself something it has to say.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

using System.Text.Json.Serialization;

public enum DashboardSectionStatus
{
    [JsonStringEnumMemberName("ok")]
    Ok,

    [JsonStringEnumMemberName("failed")]
    Failed,

    [JsonStringEnumMemberName("hidden")]
    Hidden,
}

public sealed record DashboardSection<TData>
    where TData : class
{
    private DashboardSection(DashboardSectionStatus status, TData? data)
    {
        Status = status;
        Data = data;
    }

    public DashboardSectionStatus Status { get; }

    public TData? Data { get; }

    public static DashboardSection<TData> Ok(TData data) =>
        new(DashboardSectionStatus.Ok, data ?? throw new ArgumentNullException(nameof(data)));

    public static DashboardSection<TData> Failed() => new(DashboardSectionStatus.Failed, null);

    public static DashboardSection<TData> Hidden() => new(DashboardSectionStatus.Hidden, null);
}

public sealed record AdminDashboardDto(
    DateTimeOffset GeneratedAt,
    DashboardSection<DashboardSystemHealthDto> SystemHealth,
    DashboardSection<DashboardErpDto> Erp,
    DashboardSection<DashboardPeopleAndAccessDto> PeopleAndAccess,
    DashboardSection<DashboardSecurityDto> Security,
    DashboardSection<DashboardRecentActivityDto> RecentActivity,
    DashboardSection<DashboardNeedsAttentionDto> NeedsAttention);

public sealed record DashboardViewer(Guid? UserId, IReadOnlySet<string> Permissions)
{
    public bool HasPermission(string permission) => Permissions.Contains(permission);
}

public sealed record DashboardRequest(DashboardViewer Viewer, DateTimeOffset AsOf);

public sealed record DashboardSectionResults(
    DashboardSection<DashboardSystemHealthDto> SystemHealth,
    DashboardSection<DashboardErpDto> Erp,
    DashboardSection<DashboardPeopleAndAccessDto> PeopleAndAccess,
    DashboardSection<DashboardSecurityDto> Security,
    DashboardSection<DashboardRecentActivityDto> RecentActivity);

public interface IDashboardSectionHandler<TData>
    where TData : class
{
    Task<TData> RunAsync(DashboardRequest request, CancellationToken ct);
}

public interface INeedsAttentionSectionHandler
{
    Task<DashboardNeedsAttentionDto> RunAsync(
        DashboardRequest request, DashboardSectionResults others, CancellationToken ct);
}

public interface IGetAdminDashboardHandler
{
    Task<AdminDashboardDto> HandleAsync(CancellationToken ct);
}
