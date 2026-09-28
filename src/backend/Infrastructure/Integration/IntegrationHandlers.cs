// Listing, editing and testing an integration's connection.
//
// THE TEST IS THE REASON THIS SCREEN IS WORTH BUILDING. Anyone can type a new address into a form; the question
// that matters is whether it works, and without an answer on the spot the next thing that discovers a wrong URL
// is the nightly import, hours later, reporting something that reads like the other ministry being down.
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

namespace MotsSupplierPortal.Infrastructure.Integration;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Integration;
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

        return [.. rows.Select(row => IntegrationMapping.View(row, SourceOf(row, erp)))];
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
    public async Task<IntegrationView?> HandleAsync(
        string key, UpdateIntegrationRequest request, CancellationToken ct)
    {
        var row = await db.IntegrationConnections.FirstOrDefaultAsync(c => c.Key == key, ct);
        if (row is null) return null;

        var cipherText = string.IsNullOrWhiteSpace(request.ApiSecret)
            ? null
            : cipher.Protect(request.ApiSecret);

        row.Update(request.BaseUrl, request.ApiKey, cipherText, request.IsEnabled, scope.UserId ?? Guid.Empty);

        await audit.LogAsync(
            aggregateType: "IntegrationConnection",
            aggregateId: row.Id,
            action: "IntegrationConnectionUpdated",
            ct: ct);

        await db.SaveChangesAsync(ct);

        return IntegrationMapping.View(row, nameof(ErpConnectionSource.Database));
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

internal static class IntegrationMapping
{
    public static IntegrationView View(IntegrationConnection row, string source) => new(
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
        row.LastSyncSucceeded,
        row.LastSyncSummary);
}
