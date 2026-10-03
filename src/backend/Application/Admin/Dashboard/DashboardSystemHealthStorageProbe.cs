// The on-demand probe of the object store and the virus scanner, offered beside the system health section and on
// the operations page's storage card.
//
// It is a separate request, made only when somebody presses the button, because it is the one read on these
// screens that contacts two services outside the database, and a screen that refreshes itself every minute in
// every open tab would otherwise make those calls all day for nobody.
//
// Each answer is a plain yes or no. Anything other than a timely, healthy answer is no, and no exception text or
// host name comes back with it, because this answer goes to a browser and the readiness endpoint and the server
// log already carry the diagnostic detail. The whole probe gives up after ten seconds.
//
// CheckedAt is when the server asked, so a screen showing an old answer can say how old it is.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

public sealed record DashboardStorageProbeDto(
    bool ObjectStorageReachable,
    bool VirusScannerReachable,
    DateTimeOffset CheckedAt);

public interface IProbeDashboardStorageHandler
{
    Task<DashboardStorageProbeDto> HandleAsync(CancellationToken ct);
}
