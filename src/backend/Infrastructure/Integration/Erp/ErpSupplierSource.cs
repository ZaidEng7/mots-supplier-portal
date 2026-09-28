// Fetching the supplier list from the ERP.
//
// THE WHOLE LIST IS READ IN ONE CALL, no paging and no modified-since. Seven Gates has about eighty suppliers,
// which is a small master list by the ERP's own definition, and incremental machinery over eighty rows would be
// state to keep correct in exchange for nothing. It also removes a failure mode worth removing: a high-water
// mark that drifts, or a page boundary that moves while being walked, both lose rows silently.
//
// THE HEADER SCHEME IS THE LITERAL WORD "token" IN LOWER CASE, followed by the key and secret joined by a colon.
// The ERP's documentation is explicit that the scheme keyword is case-sensitive even though the header name is
// not, and a capitalised "Token" answers 403 with an empty body - which reads exactly like a missing header and
// sends the next person looking at the wrong thing.
//
// ADDRESS AND EMAIL MAY NOT BE ON THE SUPPLIER AT ALL. In this ERP a supplier's street and mailbox are separate
// Address and Contact records joined through Dynamic Link, and the supplier's own email_id and mobile_no are a
// second place the same fact may live. This reads the supplier's own fields, and carries the names of the linked
// primary records so the caller can decide whether a second read is worth making. It deliberately does not chase
// those links yet: the test instance has no addresses at all, Dynamic Link is refused to the current credential,
// and a join written against data nobody has seen is a guess with a build behind it.
//
// A ROW WITH NO NAME IS NOT DROPPED. It would be tempting, because a supplier with no name is useless, but the
// import is the layer that decides what is unusable and says so in its report. Dropping it here would make it
// vanish from a count that somebody is going to reconcile against the ERP by hand.
//
// TIMESTAMPS GO THROUGH ErpServerTime and the zone comes from configuration, because the ERP sends local time
// with no offset. The reason that matters, and what it corrupts if it is wrong, is written there.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpSupplierSource(
    HttpClient client,
    IOptions<ErpOptions> options,
    ILogger<ErpSupplierSource> logger) : IErpSupplierSource
{
    public const string HttpClientName = "Erp";

    private static readonly string[] SupplierFields =
    [
        "name", "supplier_name", "supplier_group", "supplier_type", "tax_id", "country", "email_id", "mobile_no",
        "disabled", "default_currency", "supplier_primary_address", "supplier_primary_contact", "creation",
        "modified",
    ];

    public static void Configure(HttpClient client, ErpOptions options)
    {
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("token", $"{options.ApiKey}:{options.ApiSecret}");
    }

    public async Task<IReadOnlyList<ErpSupplier>> ListSuppliersAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var zone = ErpServerTime.Zone(settings.ServerTimeZone);
        var url = ErpQuery.List("Supplier", SupplierFields, orderBy: "name asc");

        using var response = await client.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw await FailureFor(response, ct);
        }

        var envelope = await response.Content.ReadFromJsonAsync<ErpListEnvelope<ErpSupplierRecord>>(ct);
        var records = envelope?.Data ?? [];

        logger.LogInformation("Read {Count} supplier record(s) from the ERP.", records.Count);

        return records.Select(record => Map(record, zone)).ToList();
    }

    private static ErpSupplier Map(ErpSupplierRecord record, TimeZoneInfo zone) => new(
        record.Name,
        Trimmed(record.SupplierName),
        Trimmed(record.SupplierGroup),
        Trimmed(record.SupplierType),
        Trimmed(record.TaxId),
        Trimmed(record.Country),
        Trimmed(record.EmailId),
        Trimmed(record.MobileNo),
        record.Disabled is not null && record.Disabled != 0,
        Trimmed(record.DefaultCurrency),
        Trimmed(record.PrimaryAddress),
        Trimmed(record.PrimaryContact),
        ErpServerTime.TryParse(record.Creation, zone, out var created) ? created : default,
        ErpServerTime.TryParse(record.Modified, zone, out var modified) ? modified : default);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<ErpRequestException> FailureFor(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        var excType = ExcTypeOf(body);

        return new ErpRequestException(
            response.StatusCode,
            excType,
            $"The ERP refused a supplier read with {(int)response.StatusCode} {response.StatusCode}"
            + (excType is null ? "." : $" ({excType})."));
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

internal sealed record ErpListEnvelope<T>(
    [property: JsonPropertyName("data")] IReadOnlyList<T> Data);

internal sealed record ErpSupplierRecord(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("supplier_name")] string? SupplierName,
    [property: JsonPropertyName("supplier_group")] string? SupplierGroup,
    [property: JsonPropertyName("supplier_type")] string? SupplierType,
    [property: JsonPropertyName("tax_id")] string? TaxId,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("email_id")] string? EmailId,
    [property: JsonPropertyName("mobile_no")] string? MobileNo,
    [property: JsonPropertyName("disabled")] int? Disabled,
    [property: JsonPropertyName("default_currency")] string? DefaultCurrency,
    [property: JsonPropertyName("supplier_primary_address")] string? PrimaryAddress,
    [property: JsonPropertyName("supplier_primary_contact")] string? PrimaryContact,
    [property: JsonPropertyName("creation")] string? Creation,
    [property: JsonPropertyName("modified")] string? Modified);
