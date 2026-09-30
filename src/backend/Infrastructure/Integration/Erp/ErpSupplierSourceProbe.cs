// Asking the ERP whether the import can run with the credentials currently in force.
//
// IT READS WHAT THE IMPORT READS, one record of each, with the import's own field lists and filters: a supplier, a
// contact filtered on its link to a supplier, and an address filtered the same way. Reading one Company record, the
// call the ERP's own documentation names for a connection check, would prove the address and the key and nothing
// about the import: Seven Gates grants access per record type, so a credential could pass it with no right to read
// suppliers at all, and the next hourly import would fail on a connection the screen had just called good.
//
// EVERY READ IS TRIED AND REPORTED, rather than stopping at the first refusal. An administrator asking the ERP team for
// access needs the whole list in one message - "suppliers yes, contacts yes, addresses no" - not one missing permission
// per round of testing.
//
// ONE RECORD EACH, NEVER THE LIST. A test that fetched every supplier would take as long as an import; a single record
// asks the same permission question.
//
// A REFUSAL IS ONLY CALLED A MISSING PERMISSION WHEN IT IS ONE. A 403 sends the administrator to the ERP team for
// access; a rejected key sends them back to the token they pasted; anything else - a 404 from a wrong path, a 502 from
// an ERP that is down, a login page answering 200 - means the address does not lead to the ERP's data. Telling all of
// those "needs read access" would send somebody to ask for rights the account already has.
//
// A 200 COUNTS ONLY IF IT IS THE ERP'S LIST. An address copied from the browser can land on a page that answers 200 in
// HTML, and the import would then fail on the first character of it.
//
// IT REPORTS RATHER THAN THROWS. "No" is a legitimate answer to "can the import run", and the caller stores the answer
// either way. That includes an address typed without its http:// - an exception there would leave the screen showing
// the previous test's answer, which may be a green one for a different address. An address that cannot be reached at
// all is reported once, not three times, since nothing more is learned from the second and third attempts.
//
// THE CREDENTIAL AND THE REFUSAL'S exc_type COME FROM ErpWire, as for the import, so the test sends the header the
// import sends. It builds and sends its own request, because it reports an unreachable address rather than throwing.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Net;
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
    private static readonly (string Label, string Url)[] Reads =
    [
        ("suppliers", ErpQuery.List(
            "Supplier", ErpSupplierSource.SupplierFields, limitPageLength: 1, orderBy: ErpSupplierSource.SupplierOrder)),
        ("contacts", ErpQuery.List(
            "Contact", ErpSupplierSource.ContactFields, ErpSupplierSource.LinkedToASupplier, limitPageLength: 1)),
        ("addresses", ErpQuery.List(
            "Address", ErpSupplierSource.AddressFields, ErpSupplierSource.LinkedToASupplier, limitPageLength: 1)),
    ];

    private enum Outcome { Read, Refused, KeyRejected, NotTheErp }

    private sealed record ReadAnswer(string Label, Outcome Outcome, string Detail);

    public async Task<IntegrationTestResult> TryReachAsync(CancellationToken ct)
    {
        var connection = await connections.CurrentAsync(ct);

        if (connection is null)
        {
            return new IntegrationTestResult(false, "No address is configured.");
        }

        if (!Uri.TryCreate(connection.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            return new IntegrationTestResult(
                false, $"\"{connection.BaseUrl}\" is not a full address. It needs to start with http:// or https://.");
        }

        var answers = new List<ReadAnswer>();

        foreach (var (label, url) in Reads)
        {
            try
            {
                answers.Add(await ReadAsync(connection, new Uri(baseUri, url), label, ct));
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                if (answers.Count == 0)
                {
                    return new IntegrationTestResult(false, $"Could not reach {connection.BaseUrl}: {Reason(exception)}");
                }

                answers.Add(new ReadAnswer(label, Outcome.NotTheErp, $"no answer: {Reason(exception)}"));
                break;
            }
        }

        return Verdict(connection, answers);
    }

    private async Task<ReadAnswer> ReadAsync(ErpConnection connection, Uri uri, string label, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = ErpWire.Credential(connection);

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var excType = ErpWire.ExcTypeOf(body);
        var status = $"{(int)response.StatusCode} {excType ?? response.StatusCode.ToString()}";

        if (response.IsSuccessStatusCode)
        {
            return IsErpList(body)
                ? new ReadAnswer(label, Outcome.Read, status)
                : new ReadAnswer(label, Outcome.NotTheErp, $"{status} {response.Content.Headers.ContentType?.MediaType}".TrimEnd());
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized || excType == "AuthenticationError")
        {
            return new ReadAnswer(label, Outcome.KeyRejected, status);
        }

        return new ReadAnswer(label, response.StatusCode == HttpStatusCode.Forbidden ? Outcome.Refused : Outcome.NotTheErp, status);
    }

    private static IntegrationTestResult Verdict(ErpConnection connection, List<ReadAnswer> answers)
    {
        var failed = answers.Where(a => a.Outcome != Outcome.Read).ToList();
        var readable = answers.Where(a => a.Outcome == Outcome.Read).Select(a => a.Label).ToList();
        var listed = string.Join(", ", failed.Select(a => $"{a.Label} ({a.Detail})"));

        if (failed.Count == 0)
        {
            return new IntegrationTestResult(
                true,
                $"Reached {connection.BaseUrl} and read suppliers, contacts and addresses - everything the import needs."
                + (connection.IsEnabled ? string.Empty : " The connection is switched off, so the import will not run until it is switched on."));
        }

        if (failed.Find(a => a.Outcome == Outcome.KeyRejected) is { } rejected)
        {
            return new IntegrationTestResult(
                false,
                $"Reached {connection.BaseUrl}, but it did not accept the key and secret ({rejected.Detail}). "
                + "Check both against the API token the ERP issued for the portal.");
        }

        if (failed.TrueForAll(a => a.Outcome == Outcome.Refused))
        {
            return new IntegrationTestResult(
                false,
                $"Reached {connection.BaseUrl}, but it refused to read {listed}"
                + (readable.Count > 0
                    ? $"; {string.Join(" and ", readable)} can be read. The import needs read access on all three."
                    : ". Either the account has no read access on any of them, or the ERP is not accepting the key and secret."));
        }

        return new IntegrationTestResult(
            false,
            $"Reached {connection.BaseUrl}, but {listed} did not come back as the ERP's data. "
            + "Check that the address is the ERP's own, without a page path after it, and that the ERP is running.");
    }

    private static bool IsErpList(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // A certificate the portal does not trust surfaces as "The SSL connection could not be established, see inner
    // exception", and the screen is the only place the answer goes - so the inner exception's words are added, or the
    // administrator cannot tell a certificate problem from a network one.
    private static string Reason(Exception exception) =>
        exception is HttpRequestException { InnerException: { } inner }
        && !exception.Message.Contains(inner.Message, StringComparison.Ordinal)
            ? $"{exception.Message} {inner.Message}"
            : exception.Message;
}
