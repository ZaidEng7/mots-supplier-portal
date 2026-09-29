// Asking the ERP whether the import can run with the credentials currently in force.
//
// IT READS WHAT THE IMPORT READS, one record of each, in the same way: a supplier, a contact filtered on its link to a
// supplier, and an address filtered the same way. The first version read one Company record, the call the ERP's own
// documentation names for a connection check - and a green result then proved the address and the key were right and
// nothing about the import. Seven Gates grants access per record type, so a credential could pass that check with no
// right to read suppliers at all, and the next night's import would fail on a connection the screen had just called
// good.
//
// EVERY READ IS TRIED AND REPORTED, rather than stopping at the first refusal. An administrator asking the ERP team for
// access needs the whole list in one message - "suppliers yes, contacts yes, addresses no" - not one missing permission
// per round of testing.
//
// ONE RECORD EACH, NEVER THE LIST. A test that fetched every supplier would take as long as an import; a single record
// asks the same permission question.
//
// IT REPORTS RATHER THAN THROWS. "No" is a legitimate answer to "can the import run", and the caller stores the answer
// either way. A refusal carries the ERP's own exception type, because "PermissionError" tells an administrator to ask
// the other team and a generic failure sends them to check their typing. An address that cannot be reached at all is
// reported once, not three times, since nothing more is learned from the second and third attempts.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Net.Http.Headers;
using System.Text.Json;
using MotsSupplierPortal.Application.Integration;

public interface IErpSupplierSourceProbe
{
    Task<IntegrationTestResult> TryReachAsync(CancellationToken ct);
}

public sealed class ErpSupplierSourceProbe(
    HttpClient client,
    IErpConnectionProvider connections) : IErpSupplierSourceProbe
{
    private static readonly IReadOnlyList<IReadOnlyList<object>> LinkedToASupplier =
        [["Dynamic Link", "link_doctype", "=", "Supplier"]];

    private static readonly (string Label, string Url)[] Reads =
    [
        ("suppliers", ErpQuery.List("Supplier", ["name"], limitPageLength: 1)),
        ("contacts", ErpQuery.List("Contact", ["name", "`tabDynamic Link`.link_name"], LinkedToASupplier, limitPageLength: 1)),
        ("addresses", ErpQuery.List("Address", ["name", "`tabDynamic Link`.link_name"], LinkedToASupplier, limitPageLength: 1)),
    ];

    public async Task<IntegrationTestResult> TryReachAsync(CancellationToken ct)
    {
        var connection = await connections.CurrentAsync(ct);

        if (connection is null)
        {
            return new IntegrationTestResult(false, "No address is configured.");
        }

        var baseUri = new Uri(connection.BaseUrl.TrimEnd('/') + "/");
        var answers = new List<(string Label, string? Refusal)>();

        foreach (var (label, url) in Reads)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, url));
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("token", $"{connection.ApiKey}:{connection.ApiSecret}");

                using var response = await client.SendAsync(request, ct);

                answers.Add((label, response.IsSuccessStatusCode
                    ? null
                    : $"{(int)response.StatusCode} {ExcTypeOf(await response.Content.ReadAsStringAsync(ct))}".TrimEnd()));
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return new IntegrationTestResult(false, $"Could not reach {connection.BaseUrl}: {exception.Message}");
            }
        }

        var refused = answers.Where(a => a.Refusal is not null).ToList();

        if (refused.Count == 0)
        {
            return new IntegrationTestResult(
                true,
                $"Reached {connection.BaseUrl} and read suppliers, contacts and addresses - everything the import needs.");
        }

        var readable = answers.Where(a => a.Refusal is null).Select(a => a.Label).ToList();

        return new IntegrationTestResult(
            false,
            $"Reached {connection.BaseUrl}, but it refused to read "
            + string.Join(", ", refused.Select(r => $"{r.Label} ({r.Refusal})"))
            + (readable.Count > 0 ? $"; {string.Join(" and ", readable)} can be read" : string.Empty)
            + ". The import needs read access on all three.");
    }

    private static string? ExcTypeOf(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("exc_type", out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
