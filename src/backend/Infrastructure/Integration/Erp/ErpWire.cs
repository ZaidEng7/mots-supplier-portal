// The wire code every ERP client shares: the connection to use, the request with its credential, sending it, and
// reading a refusal.
//
// ONE COPY, BECAUSE THE DETAIL MOST LIKELY TO BE TIDIED AWAY LIVES HERE. ErpSupplierSource and ErpSupplierSourceProbe
// each built the credential header and each parsed exc_type, and the writer would have made a third copy. Each client
// keeps its own reasons for what it reads and what it says; this file only does the shared part once.
//
// THE HEADER SCHEME IS THE LITERAL WORD "token" IN LOWER CASE, followed by the key and secret joined by a colon. The
// ERP's documentation is explicit that the scheme keyword is case-sensitive even though the header name is not, and a
// capitalised "Token" answers 403 with an empty body - which reads exactly like a missing header and sends the next
// person looking at the wrong thing.
//
// THE CREDENTIAL IS ATTACHED TO EACH REQUEST, NOT TO THE CLIENT, because the address and credential come from a row an
// administrator can edit while the application is running. A client carrying them on its DefaultRequestHeaders would
// keep using whatever it was built with until something restarted it - which is exactly the behaviour the integrations
// screen exists to remove. An address saved without its http:// is refused by Request before anything is sent.
//
// A SERVER THAT DOES NOT ANSWER AT ALL IS REPORTED AS THE ERP'S FAILURE, a 502 ErpRequestException. A refused
// connection, an address that does not resolve and a timeout throw a transport exception rather than returning an
// error, and one let through surfaces as a 500, which tells an administrator the portal is broken. That is the most
// likely failure of all right after somebody changes the address on the integrations screen, so it is the worst one to
// misreport. The request's method travels with it, because for a create "no answer" means the ERP may or may not have
// made the record, and ErpFailure.ClassifyPush reads it that way. A timeout that is really the caller cancelling is
// left alone: that is not the ERP failing.
//
// A REFUSAL KEEPS WHAT THE ERP SAID. exc_type names the kind of error and is what tells a missing header from a
// credential without rights, since both answer 403. _server_messages holds the sentence the ERP would have shown its
// own user - "Value missing for Supplier: Supplier Group" - as JSON inside a string inside JSON, each message an object
// of its own with HTML in its text. It is unwrapped, stripped of tags and joined, because it is the only part of a
// refusal that tells an administrator what to change. Where it is missing, the exception line stands in for it. A body
// that is not the ERP's JSON, such as a proxy's HTML page, gives neither, and the status alone speaks.
//
// A BODY IS SENT AS READABLE UTF-8, with Arabic names and a phone's "+" as they are rather than as \u escapes. Both
// are valid JSON and the ERP reads either, but a request somebody has to read in a log should say what it sent. The
// escaping it relaxes exists for JSON embedded in an HTML page, which a request body never is.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal sealed record ErpRefusal(HttpStatusCode Status, string? ExcType, string? ErpMessage);

internal static partial class ErpWire
{
    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task<ErpConnection> RequireConnectionAsync(IErpConnectionProvider connections, CancellationToken ct)
    {
        var connection = await connections.CurrentAsync(ct);

        if (connection is null || !connection.IsEnabled)
        {
            throw new ErpNotConfiguredException();
        }

        return connection;
    }

    public static AuthenticationHeaderValue Credential(ErpConnection connection) =>
        new("token", $"{connection.ApiKey}:{connection.ApiSecret}");

    public static HttpRequestMessage Request(ErpConnection connection, HttpMethod method, string path, JsonNode? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(connection.BaseUrl.TrimEnd('/') + "/"), path));

        request.Headers.Authorization = Credential(connection);

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(Readable), Encoding.UTF8, "application/json");
        }

        return request;
    }

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, ErpConnection connection, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await client.SendAsync(request, ct);
        }
        catch (HttpRequestException exception)
        {
            throw Unreachable(connection, request.Method, exception);
        }
        catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw Unreachable(connection, request.Method, exception);
        }
    }

    public static ErpRequestException Unreachable(ErpConnection connection, HttpMethod method, Exception exception) =>
        new(HttpStatusCode.BadGateway, null, $"The ERP at {connection.BaseUrl} could not be reached: {exception.Message}",
            method: method);

    public static async Task<ErpRefusal> RefusalOf(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        return new ErpRefusal(response.StatusCode, ExcTypeOf(body), ErpMessageOf(body));
    }

    public static string? ExcTypeOf(string body) => StringProperty(body, "exc_type");

    public static string? ErpMessageOf(string body)
    {
        if (StringProperty(body, "_server_messages") is { } wrapped && ServerMessages(wrapped) is { Count: > 0 } messages)
        {
            return string.Join(" ", messages);
        }

        return StringProperty(body, "exception") is { } exception ? Plain(exception) : null;
    }

    private static List<string>? ServerMessages(string wrapped)
    {
        try
        {
            using var list = JsonDocument.Parse(wrapped);

            if (list.RootElement.ValueKind != JsonValueKind.Array) return null;

            var messages = new List<string>();

            foreach (var item in list.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;

                var text = StringProperty(item.GetString()!, "message") ?? item.GetString();

                if (Plain(text) is { Length: > 0 } plain)
                {
                    messages.Add(plain);
                }
            }

            return messages;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? StringProperty(string body, string name)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Plain(string? text) =>
        text is null ? null : Whitespace().Replace(WebUtility.HtmlDecode(Tag().Replace(text, " ")), " ").Trim();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
