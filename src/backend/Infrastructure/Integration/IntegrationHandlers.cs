// Listing, editing and testing an integration's connection.
//
// THE TEST IS THE REASON THIS SCREEN IS WORTH BUILDING. Anyone can type a new address into a form; the question
// that matters is whether it works, and without an answer on the spot the next thing that discovers a wrong URL
// is the next scheduled import, up to an hour later, reporting something that reads like the other ministry being down.
//
// IT TESTS THE SAVED VALUES, NOT THE TYPED ONES. Save then test, in that order, so what gets tested is what the
// application will actually use. Testing unsaved input would prove a URL somebody typed and then navigated away
// from.
//
// THE TEST RESULT IS STORED because somebody changes an address on a Friday and leaves. The next person needs to
// know it was tested and what came back, not merely that the fields look filled in.
//
// A FAILED TEST IS NOT AN ERROR RESPONSE. The request to test succeeded; the answer is "no". Returning 502 here
// would make a working screen look broken and would lose the detail, which is the part worth reading - "403
// PermissionError" tells an administrator to ask the other team rather than to check their typing.
//
// THE SECRET IS ENCRYPTED HERE AND NOWHERE ELSE ON THE WAY IN, and is never read back on the way out. Both halves
// of that are in IntegrationViews and SecretCipher.
//
// EVERY EDIT IS AUDITED WITH THE SAVE, before the response is written. Changing where this product sends a
// ministry's credential is exactly the kind of act somebody asks about six months later.
//
// TURNING THE SUPPLIER WRITES ON OR OFF, OR CHANGING THEIR GROUP, IS AUDITED ON ITS OWN ROW, with the person as the
// actor, the switch before and after as the states, and the group as the reason. It is the one setting here that
// makes the portal write to another ministry's system, and "who turned it on, and when" has to be answerable from
// the trail without comparing rows. A save that leaves both as they were writes no such row. A refusal from the
// connection - the switch turned on without a group - saves nothing at all, the address included, so what a person
// sees after a refused save is what was there before.
//
// THE WAITING COUNT IS READ ACROSS THE WHOLE REGISTRY. It is the number of approved suppliers the push would create
// in the ERP once the switch is on, and the push serves the deployment rather than one buying body; the screen is a
// system administrator's, gated by permission.
//
// THE GROUP LIST IS READ FROM THE ERP ON REQUEST, through the import's own read client, so the same connection and
// credential answer it. A connection that is not configured, or an ERP that refuses, is the caller's to report, as
// for the import.

namespace MotsSupplierPortal.Infrastructure.Integration;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Integration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ListIntegrationsHandler(
    AppDbContext db,
    IErpConnectionProvider connections) : IListIntegrationsHandler
{
    public async Task<IReadOnlyList<IntegrationView>> HandleAsync(CancellationToken ct)
    {
        var rows = await db.IntegrationConnections.AsNoTracking().OrderBy(c => c.DisplayName).ToListAsync(ct);
        var erp = await connections.CurrentAsync(ct);
        var waiting = await db.Suppliers.CountAsync(IntegrationMapping.WaitingForErp, ct);

        return [.. rows.Select(row => IntegrationMapping.View(row, SourceOf(row, erp), waiting))];
    }

    private static string SourceOf(IntegrationConnection row, ErpConnection? erp) =>
        row.Key == IntegrationConnection.ErpKey && erp is not null
            ? erp.Source.ToString()
            : row.IsConfiguredHere ? nameof(ErpConnectionSource.Database) : "None";
}

public sealed class UpdateIntegrationHandler(
    AppDbContext db,
    SecretCipher cipher,
    IScopeContext scope,
    IAuditLogger audit) : IUpdateIntegrationHandler
{
    public async Task<UpdateIntegrationResult> HandleAsync(
        string key, UpdateIntegrationRequest request, CancellationToken ct)
    {
        var row = await db.IntegrationConnections.FirstOrDefaultAsync(c => c.Key == key, ct);
        if (row is null) return new UpdateIntegrationResult.NotFound();

        var cipherText = string.IsNullOrWhiteSpace(request.ApiSecret)
            ? null
            : cipher.Protect(request.ApiSecret);

        var userId = scope.UserId ?? Guid.Empty;
        var writesBefore = (row.CreateSuppliersInErp, row.DefaultSupplierGroup);

        row.Update(request.BaseUrl, request.ApiKey, cipherText, request.IsEnabled, userId);

        if (request.CreateSuppliersInErp is not null || request.DefaultSupplierGroup is not null)
        {
            try
            {
                row.SetSupplierCreation(
                    request.CreateSuppliersInErp ?? row.CreateSuppliersInErp,
                    request.DefaultSupplierGroup ?? row.DefaultSupplierGroup,
                    userId);
            }
            catch (DomainException refused)
            {
                return new UpdateIntegrationResult.Refused(refused.Message);
            }
        }

        await audit.LogAsync(
            aggregateType: "IntegrationConnection",
            aggregateId: row.Id,
            action: "IntegrationConnectionUpdated",
            ct: ct);

        if ((row.CreateSuppliersInErp, row.DefaultSupplierGroup) != writesBefore)
        {
            await audit.LogAsync(
                aggregateType: "IntegrationConnection",
                aggregateId: row.Id,
                action: "IntegrationSupplierCreationChanged",
                actorUserId: scope.UserId,
                fromState: IntegrationMapping.Switch(writesBefore.CreateSuppliersInErp),
                toState: IntegrationMapping.Switch(row.CreateSuppliersInErp),
                reason: row.DefaultSupplierGroup is null
                    ? "No default ERP supplier group."
                    : $"Default ERP supplier group: {row.DefaultSupplierGroup}.",
                ct: ct);
        }

        await db.SaveChangesAsync(ct);

        var waiting = await db.Suppliers.CountAsync(IntegrationMapping.WaitingForErp, ct);

        return new UpdateIntegrationResult.Updated(
            IntegrationMapping.View(row, nameof(ErpConnectionSource.Database), waiting));
    }
}

public sealed class TestIntegrationHandler(
    AppDbContext db,
    IErpSupplierSourceProbe probe) : ITestIntegrationHandler
{
    public async Task<IntegrationTestResult?> HandleAsync(string key, CancellationToken ct)
    {
        var row = await db.IntegrationConnections.FirstOrDefaultAsync(c => c.Key == key, ct);
        if (row is null) return null;

        var result = await probe.TryReachAsync(ct);

        row.RecordTest(result.Succeeded, result.Detail);
        await db.SaveChangesAsync(ct);

        return result;
    }
}

public sealed class ListErpSupplierGroupsHandler(IErpSupplierSource source) : IListErpSupplierGroupsHandler
{
    public async Task<ErpSupplierGroupsView?> HandleAsync(string key, CancellationToken ct)
    {
        if (key != IntegrationConnection.ErpKey) return null;

        return new ErpSupplierGroupsView(await source.ListSupplierGroupsAsync(ct));
    }
}

// The view of a connection, and the suppliers waiting for the ERP. WaitingForErp is what SupplierErpPushJob would push
// once the switch is on, whenever each is due: its own Pushable rule, an approved supplier in service whose push is
// Requested or Linked. A supplier a person has suspended or deactivated is not counted, because it is not pushed.
internal static class IntegrationMapping
{
    public static readonly Expression<Func<Supplier, bool>> WaitingForErp = SupplierErpPushJob.Pushable;

    public static IntegrationView View(IntegrationConnection row, string source, int suppliersWaitingForErp) => new(
        row.Key,
        row.DisplayName,
        row.BaseUrl,
        row.ApiKey,
        row.HasSecret,
        row.SecretSetAt,
        row.IsEnabled,
        source,
        row.UpdatedAt,
        row.LastTestedAt,
        row.LastTestSucceeded,
        row.LastTestDetail,
        row.LastSyncAt,
        row.LastSyncOutcome?.ToString(),
        row.LastSyncSummary,
        row.CreateSuppliersInErp,
        row.DefaultSupplierGroup,
        row.Key == IntegrationConnection.ErpKey ? suppliersWaitingForErp : 0);

    public static string Switch(bool on) => on ? "On" : "Off";
}
