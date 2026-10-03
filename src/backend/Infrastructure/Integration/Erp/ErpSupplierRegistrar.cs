// Writing a supplier that registered in the portal into the ERP, and the reads the push needs around the writes.
//
// THE CALLS ARE THE ERP COLLEAGUE'S, and IErpSupplierRegistrar lists them in his order. A create is a POST to
// api/resource/<record type>, a change a PUT to api/resource/<record type>/<name>, and a read a GET; both path segments
// are percent-encoded as ErpQuery encodes a list read. A create answers with the new record, and its name is read from
// data.name, because on the real ERP that name is minted by a naming series and the portal never chooses it.
//
// NOTHING IS WRITTEN WHILE THE WRITE SWITCH IS OFF, OR TO A SERVER THE DEPLOYMENT HAS NOT LISTED. Every create and
// change asks the connection first and throws ErpWritesOffException before a request exists, whoever the caller is,
// when the switch is off or the connection's server is not in Erp:WriteHosts; ErpWritesOffException has the reason. The
// reads run either way, as the import's do. No connection at all is ErpNotConfiguredException, as for the import.
//
// EVERY FAILURE IS AN ErpRequestException THAT KNOWS ITS METHOD, so its PushKind sorts it as ErpFailure describes:
// permission, already exists, permanent, outcome unknown or transient. A refusal's message carries the ERP's own
// sentence from _server_messages, which is what a person reads to fix it. A create that answers 200 without a name,
// or any answer that is not the ERP's JSON, is reported as a 502, which for a create means the outcome is unknown and
// for a read means try again. No answer at all is a 502 too, through ErpWire.
//
// EACH CALL HAS 30 SECONDS, the per-call limit INTEGRATION-ARCHITECTURE sets, on the typed client registered in
// Startup. A create that runs past it may still finish on the ERP's side, which is why a timeout on a POST is an
// unknown outcome and never a reason to post again.
//
// THE FIELD LIST COMES FROM THE ERP'S OWN FORM METHOD, frappe.desk.form.load.getdoctype, whose first document is the
// record type with its custom fields and property changes applied. A field's length there is 0 when the ERP keeps
// its default, which for the text and link types is 140 characters, so 140 is what the payload is measured against.
//
// THE PORTAL USERS ARE READ, THEN REPLACED WHOLE. A PUT replaces a child table rather than adding to it, so the
// Supplier is read first and every row it already has is sent back as it came, with the new user after them. A row
// left out would be deleted by the ERP. A user already on the list is not sent again, so a resumed push is harmless.
//
// "CREATED BY THE PORTAL" MEANS OWNED BY THE API USER, which ErpWire asks the ERP for, as the import does, and created
// at or after a time written in the ERP's own zone, because "creation" is stored in its local time with no offset.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpSupplierRegistrar(
    HttpClient client,
    IErpConnectionProvider connections,
    IOptions<ErpOptions> options) : IErpSupplierRegistrar
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    internal const string FieldListMethod = "frappe.desk.form.load.getdoctype";
    internal const int DefaultTextLength = 140;

    internal static readonly string[] MatchFields = ["name", "supplier_name", "tax_id"];
    internal static readonly string[] LinkedAddressFields = ["name", "address_type", "address_line1", "city"];
    internal static readonly string[] LinkedContactFields = ["name", "email_id", "user"];

    private static readonly HashSet<string> DefaultLengthTypes =
        new(["Data", "Link", "Dynamic Link", "Select", "Read Only", "Password", "Phone", "Autocomplete"], StringComparer.Ordinal);

    public async Task<ErpRecordFields> ReadFieldsAsync(string recordType, CancellationToken ct)
    {
        var connection = await ErpWire.RequireConnectionAsync(connections, ct);
        var what = $"the {recordType} field list read";
        var root = await ReadAsync(connection, ErpQuery.Call(FieldListMethod, ("doctype", recordType)), what, ct);

        var meta = (Member(root, "docs") as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(doc => Text(doc["name"]) == recordType);

        if (meta?["fields"] is not JsonArray fields)
        {
            throw NotTheErps(what, HttpMethod.Get);
        }

        return new ErpRecordFields(recordType, fields.OfType<JsonObject>().Select(Definition).OfType<ErpFieldDefinition>());
    }

    public async Task<IReadOnlyList<ErpSupplierMatch>> FindSuppliersByTaxIdAsync(string taxId, CancellationToken ct)
    {
        var connection = await ErpWire.RequireConnectionAsync(connections, ct);
        var url = ErpQuery.List(ErpRecordType.Supplier, MatchFields, [["tax_id", "=", taxId]]);

        return await ListAsync(connection, url, "the Supplier search by tax number", Match, ct);
    }

    public async Task<IReadOnlyList<ErpSupplierMatch>> FindSuppliersCreatedByPortalAsync(
        string supplierName, DateTimeOffset since, CancellationToken ct)
    {
        var connection = await ErpWire.RequireConnectionAsync(connections, ct);
        var owner = await ErpWire.ApiUserAsync(client, connection, ct);
        var createdSince = ErpServerTime.Format(since, ErpServerTime.Zone(options.Value.ServerTimeZone));

        var url = ErpQuery.List(
            ErpRecordType.Supplier,
            MatchFields,
            [["supplier_name", "=", supplierName], ["owner", "=", owner], ["creation", ">=", createdSince]],
            orderBy: "creation asc");

        return await ListAsync(connection, url, "the Supplier search by the portal's own creates", Match, ct);
    }

    public Task<string> CreateSupplierAsync(JsonObject body, CancellationToken ct) =>
        CreateAsync(ErpRecordType.Supplier, body, ct);

    public Task<string> CreateAddressAsync(JsonObject body, CancellationToken ct) =>
        CreateAsync(ErpRecordType.Address, body, ct);

    public Task<string> CreateContactAsync(JsonObject body, CancellationToken ct) =>
        CreateAsync(ErpRecordType.Contact, body, ct);

    public Task<string> CreateUserAsync(JsonObject body, CancellationToken ct) =>
        CreateAsync(ErpRecordType.User, body, ct);

    public async Task<string?> FindUserAsync(string email, CancellationToken ct)
    {
        var connection = await ErpWire.RequireConnectionAsync(connections, ct);
        var url = ErpQuery.List(ErpRecordType.User, ["name"], [["email", "=", email]]);
        var users = await ListAsync(connection, url, "the User search by email", row => Text(row["name"]), ct);

        return users.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ErpLinkedAddress>> ListLinkedAddressesAsync(string erpSupplierName, CancellationToken ct)
    {
        var connection = await ErpWire.RequireConnectionAsync(connections, ct);
        var url = ErpQuery.List(ErpRecordType.Address, LinkedAddressFields, LinkedTo(erpSupplierName));

        return await ListAsync(
            connection,
            url,
            "the read of the Supplier's addresses",
            row => Text(row["name"]) is { } name
                ? new ErpLinkedAddress(name, Text(row["address_type"]), Text(row["address_line1"]), Text(row["city"]))
                : null,
            ct);
    }

    public async Task<IReadOnlyList<ErpLinkedContact>> ListLinkedContactsAsync(string erpSupplierName, CancellationToken ct)
    {
        var connection = await ErpWire.RequireConnectionAsync(connections, ct);
        var url = ErpQuery.List(ErpRecordType.Contact, LinkedContactFields, LinkedTo(erpSupplierName));

        return await ListAsync(
            connection,
            url,
            "the read of the Supplier's contacts",
            row => Text(row["name"]) is { } name ? new ErpLinkedContact(name, Text(row["email_id"]), Text(row["user"])) : null,
            ct);
    }

    public async Task AddPortalUserAsync(string erpSupplierName, string user, CancellationToken ct)
    {
        var connection = await WritableConnectionAsync(ct);
        var path = ErpQuery.Record(ErpRecordType.Supplier, erpSupplierName);
        var what = "the Supplier's portal users read";

        if (Member(await ReadAsync(connection, path, what, ct), "data") is not JsonObject supplier)
        {
            throw NotTheErps(what, HttpMethod.Get);
        }

        var rows = supplier["portal_users"] is JsonArray existing ? (JsonArray)existing.DeepClone() : new JsonArray();

        if (rows.OfType<JsonObject>().Any(row => string.Equals(Text(row["user"]), user, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        rows.Add(new JsonObject { ["user"] = user });

        using var response = await SendAsync(
            connection, HttpMethod.Put, path, new JsonObject { ["portal_users"] = rows }, "the Supplier's portal users update", ct);
    }

    public async Task SetContactUserAsync(string contactName, string user, CancellationToken ct)
    {
        var connection = await WritableConnectionAsync(ct);

        using var response = await SendAsync(
            connection,
            HttpMethod.Put,
            ErpQuery.Record(ErpRecordType.Contact, contactName),
            new JsonObject { ["user"] = user },
            "the Contact's user update",
            ct);
    }

    private async Task<string> CreateAsync(string recordType, JsonObject body, CancellationToken ct)
    {
        var connection = await WritableConnectionAsync(ct);
        var what = $"the {recordType} create";

        using var response = await SendAsync(connection, HttpMethod.Post, ErpQuery.Resource(recordType), body, what, ct);
        var created = await JsonAsync(response, what, HttpMethod.Post, ct);

        return Text(Member(Member(created, "data"), "name")) ?? throw NotTheErps(what, HttpMethod.Post);
    }

    // The connection a write may use: the switch on, and the server named in Erp:WriteHosts. ErpWriteHosts holds the
    // match, on the server's host or on its host and port, and the dashboard's yes or no asks the same one. An address
    // that does not parse is not a listed server, and the refusal then names the address as it was written.
    private async Task<ErpConnection> WritableConnectionAsync(CancellationToken ct)
    {
        var connection = await ErpWire.RequireConnectionAsync(connections, ct);
        if (!connection.CreateSuppliersInErp) throw new ErpWritesOffException();

        if (ErpWriteHosts.Lists(options.Value.WriteHosts, connection.BaseUrl)) return connection;

        throw ErpWritesOffException.ServerNotAllowed(
            Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var server) ? server.Authority : connection.BaseUrl);
    }

    private async Task<JsonNode?> ReadAsync(ErpConnection connection, string url, string what, CancellationToken ct)
    {
        using var response = await SendAsync(connection, HttpMethod.Get, url, null, what, ct);

        return await JsonAsync(response, what, HttpMethod.Get, ct);
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(
        ErpConnection connection, string url, string what, Func<JsonObject, T?> map, CancellationToken ct)
    {
        if (Member(await ReadAsync(connection, url, what, ct), "data") is not JsonArray rows)
        {
            throw NotTheErps(what, HttpMethod.Get);
        }

        return [.. rows.OfType<JsonObject>().Select(map).OfType<T>()];
    }

    private async Task<HttpResponseMessage> SendAsync(
        ErpConnection connection, HttpMethod method, string path, JsonNode? body, string what, CancellationToken ct)
    {
        using var request = ErpWire.Request(connection, method, path, body);
        var response = await ErpWire.SendAsync(client, connection, request, ct);

        if (response.IsSuccessStatusCode) return response;

        using (response)
        {
            var refusal = await ErpWire.RefusalOf(response, ct);

            throw new ErpRequestException(
                refusal.Status,
                refusal.ExcType,
                $"The ERP refused {what} with {(int)refusal.Status} {refusal.Status}"
                + (refusal.ExcType is null ? string.Empty : $" ({refusal.ExcType})")
                + (refusal.ErpMessage is null ? "." : $": {refusal.ErpMessage}"),
                refusal.ErpMessage,
                method);
        }
    }

    private static async Task<JsonNode?> JsonAsync(
        HttpResponseMessage response, string what, HttpMethod method, CancellationToken ct)
    {
        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (JsonException)
        {
            throw NotTheErps(what, method);
        }
    }

    private static ErpRequestException NotTheErps(string what, HttpMethod method) =>
        new(HttpStatusCode.BadGateway, null, $"The ERP answered {what}, but not with the ERP's data.", method: method);

    private static IReadOnlyList<IReadOnlyList<object>> LinkedTo(string erpSupplierName) =>
    [
        ["Dynamic Link", "link_doctype", "=", ErpRecordType.Supplier],
        ["Dynamic Link", "link_name", "=", erpSupplierName],
    ];

    private static ErpSupplierMatch? Match(JsonObject row) =>
        Text(row["name"]) is { } name ? new ErpSupplierMatch(name, Text(row["supplier_name"]), Text(row["tax_id"])) : null;

    private static ErpFieldDefinition? Definition(JsonObject field)
    {
        if (Text(field["fieldname"]) is not { } name) return null;

        var type = Text(field["fieldtype"]) ?? string.Empty;
        var length = field["length"] is JsonValue value && value.TryGetValue<int>(out var stated) ? stated : 0;
        var options = (Text(field["options"]) ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ErpFieldDefinition(
            name,
            type,
            options,
            length > 0 ? length : DefaultLengthTypes.Contains(type) ? DefaultTextLength : null);
    }

    private static JsonNode? Member(JsonNode? node, string name) => node is JsonObject body ? body[name] : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
}
