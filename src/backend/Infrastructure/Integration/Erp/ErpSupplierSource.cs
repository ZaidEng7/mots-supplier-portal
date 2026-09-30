// Fetching the supplier list from the ERP.
//
// THE WHOLE LIST IS READ IN ONE CALL, no paging and no modified-since. Seven Gates has about eighty suppliers,
// which is a small master list by the ERP's own definition, and incremental machinery over eighty rows would be
// state to keep correct in exchange for nothing. It also removes a failure mode worth removing: a high-water
// mark that drifts, or a page boundary that moves while being walked, both lose rows silently.
//
// THE WIRE CODE IS SHARED, in ErpWire, with the connection test and the writer: the lower-case "token" header on
// every request, a server that does not answer reported as the ERP's failure, and a refusal's exc_type read out of
// the ERP's own error envelope. The reasons for each are written there.
//
// EVERY SUPPLIER FIELD IS ASKED FOR, as "*", rather than a list. The Arabic name and the registration number live in
// fields Seven Gates added to their own ERP, and naming a field a server does not have is an error there, not an
// empty value - so a list naming them would make the import fail outright against the test instance, or against the
// real one the day somebody renames a field. "*" returns what exists, and a field that is missing reads as null.
// Those three custom fields are named by ErpSupplierPayload's constants, which the push writes them under, so the
// two directions cannot come to spell one differently.
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
// CONTACTS ARE READ EVERY TIME, not only when a supplier lacks an email or phone, because the contact also carries
// the person's name. And ADDRESSES ARE READ THE SAME WAY, in a third request filtered on the same child rows. The
// test instance had none to try this against; the real server has one billing address for nearly every supplier, and
// it answered this exact query - which ErpAddressMerge and ErpAddressMapper then turn into the portal's shape.
//
// A ROW WITH NO NAME IS NOT DROPPED. It would be tempting, because a supplier with no name is useless, but the
// import is the layer that decides what is unusable and says so in its report. Dropping it here would make it
// vanish from a count that somebody is going to reconcile against the ERP by hand.
//
// TIMESTAMPS GO THROUGH ErpServerTime and the zone comes from configuration, because the ERP sends local time
// with no offset. The reason that matters, and what it corrupts if it is wrong, is written there.
//
// THE PORTAL'S OWN API USER IS ASKED ONCE PER LIST, after the three reads, and each supplier whose owner it is is
// marked CreatedByPortal: the push made it. The read is ErpWire's, the one the push looks for its own creates with, so
// both mean the same user. The connection test does not ask it, because the ERP answers it to any credential it lets
// sign in at all, which the supplier read already proves.
//
// THE SUPPLIER GROUPS ARE A FIFTH READ, made only when the integrations screen asks for them, never by the import.
// The ERP keeps its groups as a tree, and a group that holds other groups - "All Supplier Groups" at the root - is a
// heading rather than a place to file a supplier, so only the groups that are not headings are asked for. is_group
// is a standard field of the ERP's Supplier Group, present on the test server and the real one alike.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpSupplierSource(
    HttpClient client,
    IErpConnectionProvider connections,
    IOptions<ErpOptions> options,
    ILogger<ErpSupplierSource> logger) : IErpSupplierSource
{
    public const string HttpClientName = "Erp";

    // What each read asks for is shared with ErpSupplierSourceProbe, so the connection test asks the ERP exactly what
    // the import will ask it. A field the credential may not read, or one the server does not have, then shows up in
    // the test rather than in the first import.
    internal static readonly string[] ContactFields =
    [
        "name", "full_name", "email_id", "mobile_no", "is_primary_contact", "`tabDynamic Link`.link_name",
    ];

    internal static readonly string[] AddressFields =
    [
        "name", "address_line1", "address_line2", "city", "country", "is_primary_address", "address_type", "disabled",
        "`tabDynamic Link`.link_name",
    ];

    internal static readonly string[] SupplierFields = ["*"];

    internal const string SupplierOrder = "name asc";

    internal static readonly string[] SupplierGroupFields = ["name"];

    internal static readonly IReadOnlyList<IReadOnlyList<object>> NotAHeading = [["Supplier Group", "is_group", "=", 0]];

    internal static readonly IReadOnlyList<IReadOnlyList<object>> LinkedToASupplier =
        [["Dynamic Link", "link_doctype", "=", "Supplier"]];

    private async Task<HttpResponseMessage> SendAsync(ErpConnection connection, string url, CancellationToken ct)
    {
        using var request = ErpWire.Request(connection, HttpMethod.Get, url);

        return await ErpWire.SendAsync(client, connection, request, ct);
    }

    public async Task<IReadOnlyList<ErpSupplier>> ListSuppliersAsync(CancellationToken ct)
    {
        var connection = await ErpWire.RequireConnectionAsync(connections, ct);
        var zone = ErpServerTime.Zone(options.Value.ServerTimeZone);
        var url = ErpQuery.List("Supplier", SupplierFields, orderBy: SupplierOrder);

        using var response = await SendAsync(connection, url, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw await FailureFor(response, ct);
        }

        var envelope = await response.Content.ReadFromJsonAsync<ErpListEnvelope<ErpSupplierRecord>>(ct);
        var records = envelope?.Data ?? [];

        logger.LogInformation("Read {Count} supplier record(s) from the ERP.", records.Count);

        var contacts = await ReadSupplierContactsAsync(connection, ct);
        logger.LogInformation("Read {Count} linked contact(s) from the ERP.", contacts.Count);

        var addresses = await ReadSupplierAddressesAsync(connection, ct);
        logger.LogInformation("Read {Count} linked address(es) from the ERP.", addresses.Count);

        var apiUser = await ErpWire.ApiUserAsync(client, connection, ct);
        var suppliers = records.Select(record => Map(record, zone, apiUser)).ToList();

        return ErpAddressMerge.Fill(ErpContactMerge.Fill(suppliers, contacts), addresses);
    }

    public async Task<IReadOnlyList<string>> ListSupplierGroupsAsync(CancellationToken ct)
    {
        var connection = await ErpWire.RequireConnectionAsync(connections, ct);
        var url = ErpQuery.List("Supplier Group", SupplierGroupFields, NotAHeading, orderBy: SupplierOrder);

        using var response = await SendAsync(connection, url, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw await FailureFor(response, ct, "a supplier group read");
        }

        var envelope = await response.Content.ReadFromJsonAsync<ErpListEnvelope<ErpNamedRecord>>(ct);

        return [.. (envelope?.Data ?? [])
            .Select(record => Trimmed(record.Name))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];
    }

    private async Task<IReadOnlyList<ErpSupplierAddressRow>> ReadSupplierAddressesAsync(
        ErpConnection connection, CancellationToken ct)
    {
        var url = ErpQuery.List("Address", AddressFields, LinkedToASupplier);

        using var response = await SendAsync(connection, url, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw await FailureFor(response, ct);
        }

        var envelope = await response.Content.ReadFromJsonAsync<ErpListEnvelope<ErpAddressRecord>>(ct);

        return [.. (envelope?.Data ?? [])
            .Where(record => record.SupplierName is not null)
            .Select(record => new ErpSupplierAddressRow(
                AddressName: record.Name,
                SupplierName: record.SupplierName!,
                Line1: Trimmed(record.Line1),
                Line2: Trimmed(record.Line2),
                City: Trimmed(record.City),
                Country: Trimmed(record.Country),
                IsPrimary: record.IsPrimaryAddress is not null && record.IsPrimaryAddress != 0,
                AddressType: Trimmed(record.AddressType),
                Disabled: record.Disabled is not null && record.Disabled != 0))];
    }

    private async Task<IReadOnlyList<ErpSupplierContact>> ReadSupplierContactsAsync(
        ErpConnection connection, CancellationToken ct)
    {
        var url = ErpQuery.List("Contact", ContactFields, LinkedToASupplier);

        using var response = await SendAsync(connection, url, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw await FailureFor(response, ct);
        }

        var envelope = await response.Content.ReadFromJsonAsync<ErpListEnvelope<ErpContactRecord>>(ct);

        return [.. (envelope?.Data ?? [])
            .Where(record => record.SupplierName is not null)
            .Select(record => new ErpSupplierContact(
                ContactName: record.Name,
                SupplierName: record.SupplierName!,
                Email: Trimmed(record.EmailId),
                Phone: Trimmed(record.MobileNo),
                IsPrimary: record.IsPrimaryContact is not null && record.IsPrimaryContact != 0,
                FullName: Trimmed(record.FullName)))];
    }

    // Every field is passed by name. Most of them are nullable strings, so a value placed one slot off by position
    // would compile and land in the wrong field - the registration number in the description, say - with nothing to
    // show for it until somebody read the supplier. ContactPersonName and Address are left to ErpContactMerge and
    // ErpAddressMerge, which fill them from the other two reads.
    private static ErpSupplier Map(ErpSupplierRecord record, TimeZoneInfo zone, string apiUser) => new(
        ExternalId: record.Name,
        Name: Trimmed(record.SupplierName),
        SupplierGroup: Trimmed(record.SupplierGroup),
        LegalType: Trimmed(record.SupplierType),
        TaxId: Trimmed(record.TaxId),
        Country: Trimmed(record.Country),
        Email: Trimmed(record.EmailId),
        Phone: Trimmed(record.MobileNo),
        Disabled: record.Disabled is not null && record.Disabled != 0,
        Currency: Trimmed(record.DefaultCurrency),
        PrimaryAddressName: Trimmed(record.PrimaryAddress),
        PrimaryContactName: Trimmed(record.PrimaryContact),
        CreatedAt: ErpServerTime.TryParse(record.Creation, zone, out var created) ? created : default,
        ModifiedAt: ErpServerTime.TryParse(record.Modified, zone, out var modified) ? modified : default,
        ArabicName: Trimmed(record.ArabicName),
        RegistrationNumber: Trimmed(record.RegistrationNumber),
        RegistrationType: Trimmed(record.RegistrationType),
        Description: Trimmed(record.Description),
        WorkflowState: Trimmed(record.WorkflowState),
        CreatedByPortal: string.Equals(Trimmed(record.Owner), apiUser, StringComparison.OrdinalIgnoreCase));

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<ErpRequestException> FailureFor(
        HttpResponseMessage response, CancellationToken ct, string read = "a supplier read")
    {
        var refusal = await ErpWire.RefusalOf(response, ct);

        return new ErpRequestException(
            refusal.Status,
            refusal.ExcType,
            $"The ERP refused {read} with {(int)refusal.Status} {refusal.Status}"
            + (refusal.ExcType is null ? "." : $" ({refusal.ExcType})."),
            refusal.ErpMessage);
    }
}

internal sealed record ErpListEnvelope<T>(
    [property: JsonPropertyName("data")] IReadOnlyList<T> Data);

internal sealed record ErpNamedRecord(
    [property: JsonPropertyName("name")] string? Name);

internal sealed record ErpAddressRecord(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address_line1")] string? Line1,
    [property: JsonPropertyName("address_line2")] string? Line2,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("is_primary_address")] int? IsPrimaryAddress,
    [property: JsonPropertyName("address_type")] string? AddressType,
    [property: JsonPropertyName("disabled")] int? Disabled,
    [property: JsonPropertyName("link_name")] string? SupplierName);

internal sealed record ErpContactRecord(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("full_name")] string? FullName,
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
    [property: JsonPropertyName("modified")] string? Modified,
    [property: JsonPropertyName(ErpSupplierPayload.ArabicNameField)] string? ArabicName,
    [property: JsonPropertyName(ErpSupplierPayload.RegistrationNumberField)] string? RegistrationNumber,
    [property: JsonPropertyName(ErpSupplierPayload.RegistrationTypeField)] string? RegistrationType,
    [property: JsonPropertyName("supplier_details")] string? Description,
    [property: JsonPropertyName("workflow_state")] string? WorkflowState,
    [property: JsonPropertyName("owner")] string? Owner);
