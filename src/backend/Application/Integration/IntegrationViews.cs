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
// THE LAST IMPORT IS PART OF THE VIEW because the hourly run is unattended, and a failure nobody sees looks exactly
// like a run with nothing to do. Showing it where the connection is managed puts it in front of the person who
// would fix it.
//
// SOURCE IS PART OF THE VIEW because a deployment can still be running from its own settings, and an
// administrator who saves an address and sees no change needs to be told why rather than left guessing.
//
// THE SUPPLIER WRITES ARE PART OF THE VIEW TOO: the switch that lets the portal create approved suppliers in the ERP,
// the ERP supplier group they are filed under, and how many approved suppliers are waiting to be created. The count is
// what the screen shows before somebody turns the switch on, because turning it on sends every one of them to the
// ERP within minutes, and a person agreeing to that should know whether it is two suppliers or two hundred. It counts
// what SupplierErpPushJob would push - approved, in service, and Requested or Linked - whenever each is due, and it is
// zero on any connection but the ERP's. A supplier a person has suspended or deactivated is left out, because it is not
// pushed until it is back in service.
//
// ON THE WAY IN, BOTH ARE OPTIONAL AND null MEANS "LEAVE THEM AS THEY ARE", like the secret. A caller written before
// the switch existed sends neither, and must not turn writes off, or clear the group, by saving an address. A blank
// group clears it. The connection itself refuses the switch on without a group, and a group longer than the ERP holds.
//
// THE GROUPS COME FROM THE ERP, read when the screen asks for them, because the ERP is where they are kept and a
// name typed by hand that the ERP does not have fails every create. Only groups a supplier can be filed under are
// listed; ErpSupplierSource says which.

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
    string? LastSyncOutcome = null,
    string? LastSyncSummary = null,
    bool CreateSuppliersInErp = false,
    string? DefaultSupplierGroup = null,
    int SuppliersWaitingForErp = 0);

public sealed record UpdateIntegrationRequest(
    string BaseUrl,
    string ApiKey,
    string? ApiSecret,
    bool IsEnabled,
    bool? CreateSuppliersInErp = null,
    string? DefaultSupplierGroup = null);

// What saving a connection can answer. Refused carries the connection's own sentence, such as the switch turned on
// without a group, so the screen can say which rule it broke.
public abstract record UpdateIntegrationResult
{
    public sealed record Updated(IntegrationView View) : UpdateIntegrationResult;

    public sealed record NotFound : UpdateIntegrationResult;

    public sealed record Refused(string Message) : UpdateIntegrationResult;
}

public sealed record ErpSupplierGroupsView(IReadOnlyList<string> Groups);

public sealed record IntegrationTestResult(bool Succeeded, string Detail);

public interface IListIntegrationsHandler
{
    Task<IReadOnlyList<IntegrationView>> HandleAsync(CancellationToken ct);
}

public interface IUpdateIntegrationHandler
{
    Task<UpdateIntegrationResult> HandleAsync(string key, UpdateIntegrationRequest request, CancellationToken ct);
}

public interface ITestIntegrationHandler
{
    Task<IntegrationTestResult?> HandleAsync(string key, CancellationToken ct);
}

// Null for a connection that is not the ERP's, which has no supplier groups to list.
public interface IListErpSupplierGroupsHandler
{
    Task<ErpSupplierGroupsView?> HandleAsync(string key, CancellationToken ct);
}
