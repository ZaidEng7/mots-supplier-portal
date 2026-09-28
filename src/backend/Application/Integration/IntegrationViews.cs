// What the integrations screen shows and accepts.
//
// THERE IS NO SECRET ON THE WAY OUT and there never will be. The view says whether one is set and when it was
// last changed, which is what somebody needs in order to decide whether to replace it. A screen that could
// display it is a screen somebody photographs.
//
// THE SECRET IS OPTIONAL ON THE WAY IN, and null means "leave the one that is there". The field arrives empty on
// every edit because nothing can prefill it, so treating empty as "clear it" would wipe the credential each time
// somebody fixed a typo in the address - and nothing would fail until the next run.
//
// THE LAST IMPORT IS PART OF THE VIEW because the nightly run is unattended, and a failure nobody sees looks exactly
// like a night with nothing to do. Showing it where the connection is managed puts it in front of the person who
// would fix it.
//
// SOURCE IS PART OF THE VIEW because a deployment can still be running from its own settings, and an
// administrator who saves an address and sees no change needs to be told why rather than left guessing.

namespace MotsSupplierPortal.Application.Integration;

public sealed record IntegrationView(
    string Key,
    string DisplayName,
    string BaseUrl,
    string ApiKey,
    bool HasSecret,
    DateTimeOffset? SecretSetAt,
    bool IsEnabled,
    string Source,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? LastTestedAt,
    bool? LastTestSucceeded,
    string? LastTestDetail,
    DateTimeOffset? LastSyncAt = null,
    bool? LastSyncSucceeded = null,
    string? LastSyncSummary = null);

public sealed record UpdateIntegrationRequest(
    string BaseUrl,
    string ApiKey,
    string? ApiSecret,
    bool IsEnabled);

public sealed record IntegrationTestResult(bool Succeeded, string Detail);

public interface IListIntegrationsHandler
{
    Task<IReadOnlyList<IntegrationView>> HandleAsync(CancellationToken ct);
}

public interface IUpdateIntegrationHandler
{
    Task<IntegrationView?> HandleAsync(string key, UpdateIntegrationRequest request, CancellationToken ct);
}

public interface ITestIntegrationHandler
{
    Task<IntegrationTestResult?> HandleAsync(string key, CancellationToken ct);
}
