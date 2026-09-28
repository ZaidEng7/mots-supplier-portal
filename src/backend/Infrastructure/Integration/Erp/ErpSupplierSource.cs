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
// THE EMAIL IS FETCHED FROM CONTACTS TOO, IN A SECOND REQUEST. In this ERP a supplier's email lives on a separate
// Contact record and reaches the supplier's own email_id only once somebody sets that contact as the supplier's
// primary - a different field from the "Is Primary Contact" box people actually tick. Reading only the supplier
// would have reported every supplier on the test instance as having no email while their addresses sat one table
// over. Why that matters, and which value wins, is in ErpContactMerge.
//
// THE MAPPING COMES BACK IN ONE REQUEST, not one per supplier. Listing Dynamic Link directly is refused to this
// credential, but a Contact list may be FILTERED on its Dynamic Link child rows and may project link_name out of
// them - so the whole contact-to-supplier mapping arrives in a single call whether there are three suppliers or
// eighty. That was found by trying it rather than by reading documentation, and it is the difference between two
// requests and eighty-one.
//
// ADDRESSES ARE NOT CHASED YET. The same trick should work for them, but the test instance has no Address rows at
// all - creating one fails on that server with "Language English not found", which is its own configuration and
// not ours - so a join written now would be a guess with a build behind it.
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

    private static readonly string[] ContactFields =
    [
        "name", "email_id", "mobile_no", "is_primary_contact", "`tabDynamic Link`.link_name",
    ];

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
        var suppliers = records.Select(record => Map(record, zone)).ToList();

        logger.LogInformation("Read {Count} supplier record(s) from the ERP.", suppliers.Count);

        if (suppliers.Any(supplier => supplier.Email is null || supplier.Phone is null))
        {
            var contacts = await ReadSupplierContactsAsync(ct);
            logger.LogInformation("Read {Count} linked contact(s) from the ERP.", contacts.Count);

            return ErpContactMerge.Fill(suppliers, contacts);
        }

        return suppliers;
    }

    private async Task<IReadOnlyList<ErpSupplierContact>> ReadSupplierContactsAsync(CancellationToken ct)
    {
        var url = ErpQuery.List(
            "Contact",
            ContactFields,
            [["Dynamic Link", "link_doctype", "=", "Supplier"]]);

        using var response = await client.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw await FailureFor(response, ct);
        }

        var envelope = await response.Content.ReadFromJsonAsync<ErpListEnvelope<ErpContactRecord>>(ct);

        return [.. (envelope?.Data ?? [])
            .Where(record => record.SupplierName is not null)
            .Select(record => new ErpSupplierContact(
                record.Name,
                record.SupplierName!,
                Trimmed(record.EmailId),
                Trimmed(record.MobileNo),
                record.IsPrimaryContact is not null && record.IsPrimaryContact != 0))];
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

internal sealed record ErpContactRecord(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email_id")] string? EmailId,
    [property: JsonPropertyName("mobile_no")] string? MobileNo,
    [property: JsonPropertyName("is_primary_contact")] int? IsPrimaryContact,
    [property: JsonPropertyName("link_name")] string? SupplierName);

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
