// What the ERP is sent to create a supplier that registered in the portal, or why it is not sent yet.
//
// IT IS PURE, like the import's rules: the supplier, the connection's default ERP supplier group and the ERP's field
// lists go in, and the request bodies come out, or the reason the push is held. No database and no HTTP, so each
// mapping below has a test that fails without it.
//
// WHAT GOES WHERE. Every value is trimmed, and none is ever cut.
//
//   Supplier  naming_series                 the ERP field's first option. The real ERP names suppliers by its series
//                                           SUP-.YYYY.-.#####, its only option; the test ERP has no such field and
//                                           names a supplier by its supplier_name.
//             supplier_name                 the English legal name, or the English display name when that is blank.
//                                           The import writes the ERP's name back into the display name and the legal
//                                           name, so after the first import both hold this value.
//             custom_supplier_arabic_name   the Arabic legal name, or the Arabic display name, in the field the import
//                                           reads it from.
//             supplier_type                 the legal type, which the ERP spells as the portal does: Company,
//                                           Individual or Partnership.
//             supplier_group                the connection's default group. The owner chose one group for every
//                                           supplier; the ERP team files it properly during their own approval.
//             country                       the ERP's name for the primary address's country.
//             default_currency, tax_id      as stored.
//             custom_registration_number,
//             custom_registration_type      as stored, in the fields the import reads them from.
//             supplier_details, website     the description and website, as stored.
//   Address   address_title (the supplier_name), address_type Billing, address_line1, address_line2, city, state (the
//             governorate's Arabic name), country, pincode, is_primary_address, and the link to the Supplier.
//   Contact   first_name and last_name, the primary email and primary mobile, designation, is_primary_contact, and
//             the link to the Supplier.
//   User      email, first_name, user_type Website User, the Supplier role, and no welcome email. No password is
//             sent, as the ERP colleague asked.
//
// ONLY THE PRIMARY ADDRESS AND THE PRIMARY REPRESENTATIVE GO, because those are the colleague's calls. The primary
// address is the first one added, and a supplier cannot submit without a head office.
//
// A FIELD THE ERP DOES NOT HAVE IS LEFT OUT, AND SO IS AN EMPTY VALUE. The test server has none of Seven Gates' custom
// fields and no naming series, and the real one requires fields the test one does not, so the ERP's own field list
// decides, read once per run. A value dropped for want of a field is written in Notes, so the first real create shows
// what the ERP was not given. The supplier's email and mobile are not sent on the Supplier at all: the ERP fills them
// from the primary contact, and may make a contact of its own from them, which would give the supplier two.
//
// THE PUSH IS HELD, WITH EVERY REASON AT ONCE, rather than sent to be refused: no default group, no address, a country
// the portal cannot name in the ERP's terms, a value longer than the ERP stores for that field, or a value its Select
// field does not offer. The ERP would refuse each of these too, but only after the Supplier record was made. A website
// or a job title that is too long is left out with a note instead, because neither is who the supplier is.
//
// THE COUNTRY IS NAMED FOR THE ERP, NEVER GUESSED. The address's free-text country is normalised as the ministry's feed
// does it, and only Syria has an ERP name confirmed today; anything else holds the push until one is added here.
//
// THE REPRESENTATIVE'S NAME IS SPLIT AT THE LAST SPACE: the last word is the last_name, the rest the first_name. The
// ERP's full_name joins them with a space, which is what the import reads back, so the portal gets back the name it
// sent. The user's first_name is the contact's, as the colleague's call carries first_name alone.
//
// NO USER IS MADE FOR A PLACEHOLDER EMAIL on erp-import.invalid, and the contact carries no email then either: such an
// address can never deliver, and it would plant a false one in the ERP.

namespace MotsSupplierPortal.Application.Integration;

using System.Text.Json.Nodes;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;

public sealed record ErpSupplierPayloadResult(ErpSupplierPayload? Payload, string? HeldReason)
{
    public bool IsHeld => Payload is null;
}

public sealed class ErpSupplierPayload
{
    public const string ArabicNameField = "custom_supplier_arabic_name";
    public const string RegistrationNumberField = "custom_registration_number";
    public const string RegistrationTypeField = "custom_registration_type";

    public const string BillingAddressType = "Billing";
    public const string WebsiteUserType = "Website User";
    public const string SupplierRole = "Supplier";

    private static readonly IReadOnlyDictionary<string, string> ErpCountryNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ErpAddressMapper.SyriaCode] = "Syria",
        };

    private readonly JsonObject _supplier;
    private readonly JsonObject _address;
    private readonly bool _addressLinks;
    private readonly JsonObject _contact;
    private readonly bool _contactLinks;
    private readonly JsonObject? _user;

    private ErpSupplierPayload(
        JsonObject supplier,
        JsonObject address,
        bool addressLinks,
        JsonObject contact,
        bool contactLinks,
        JsonObject? user,
        string supplierName,
        string? taxId,
        string? userEmail,
        IReadOnlyList<string> notes)
    {
        _supplier = supplier;
        _address = address;
        _addressLinks = addressLinks;
        _contact = contact;
        _contactLinks = contactLinks;
        _user = user;
        SupplierName = supplierName;
        TaxId = taxId;
        UserEmail = userEmail;
        Notes = notes;
    }

    public string SupplierName { get; }

    public string? TaxId { get; }

    public string? UserEmail { get; }

    public IReadOnlyList<string> Notes { get; }

    public JsonObject Supplier() => (JsonObject)_supplier.DeepClone();

    public JsonObject Address(string erpSupplierName) => WithLink(_address, _addressLinks, erpSupplierName);

    public JsonObject Contact(string erpSupplierName) => WithLink(_contact, _contactLinks, erpSupplierName);

    public JsonObject? User() => (JsonObject?)_user?.DeepClone();

    public static ErpSupplierPayloadResult Build(
        Supplier supplier,
        string? defaultSupplierGroup,
        ErpRecordFields supplierFields,
        ErpRecordFields addressFields,
        ErpRecordFields contactFields)
    {
        var held = new List<string>();
        var notes = new List<string>();

        var group = Clean(defaultSupplierGroup);
        if (group is null)
        {
            held.Add("No default ERP supplier group is set on the ERP connection, and the ERP needs one for every supplier.");
        }

        var address = supplier.Addresses.FirstOrDefault(a => a.IsPrimary) ?? supplier.Addresses.FirstOrDefault();
        var country = address is null ? null : ErpCountryName(address.Country);

        if (address is null)
        {
            held.Add("The supplier has no address to create in the ERP.");
        }
        else if (country is null)
        {
            held.Add($"The address's country, \"{address.Country.Trim()}\", has no ERP country name the portal knows; "
                + "only Syria is mapped.");
        }

        var representative = supplier.Representatives.FirstOrDefault(r => r.IsPrimary)
            ?? supplier.Representatives.FirstOrDefault();

        if (representative is null)
        {
            held.Add("The supplier has no representative to create as its ERP contact.");
        }

        var legal = supplier.LegalInfo;
        var supplierName = Clean(legal?.LegalNameEn) ?? supplier.DisplayNameEn.Trim();
        var (firstName, lastName) = SplitName(representative?.FullName);
        var email = Clean(representative?.Email);
        var deliverable = email is not null && !ErpImportAdmission.IsPlaceholder(email) ? email : null;
        var phone = Clean(representative?.Phone);

        var supplierBody = new Body(supplierFields, held, notes);
        supplierBody.Put("naming_series", supplierFields["naming_series"]?.Options.FirstOrDefault());
        supplierBody.Put("supplier_name", supplierName);
        supplierBody.Put(ArabicNameField, Clean(legal?.LegalNameAr) ?? supplier.DisplayNameAr);
        supplierBody.Put("supplier_type", legal?.SupplierType.ToString());
        supplierBody.Put("supplier_group", group);
        supplierBody.Put("country", country);
        supplierBody.Put("default_currency", supplier.CurrencyCode);
        supplierBody.Put("tax_id", legal?.TaxId);
        supplierBody.Put(RegistrationNumberField, legal?.RegistrationNumber);
        supplierBody.Put(RegistrationTypeField, legal?.RegistrationType);
        supplierBody.Put("supplier_details", supplier.Description);
        supplierBody.Put("website", supplier.Website, dropWhenTooLong: true);

        var addressBody = new Body(addressFields, held, notes);
        if (address is not null)
        {
            var governorate = GovernorateName(address.RegionCode);
            if (governorate is null && addressFields.Has("state"))
            {
                notes.Add($"The governorate code \"{address.RegionCode}\" has no name the portal knows, so the address's "
                    + "state is left out.");
            }

            addressBody.Put("address_title", supplierName);
            addressBody.Put("address_type", BillingAddressType);
            addressBody.Put("address_line1", address.Line1);
            addressBody.Put("address_line2", address.Line2);
            addressBody.Put("city", address.City);
            addressBody.Put("state", governorate);
            addressBody.Put("country", country);
            addressBody.Put("pincode", address.PostalCode);
            addressBody.Put("is_primary_address", 1);
        }

        var contactBody = new Body(contactFields, held, notes);
        contactBody.Put("first_name", firstName);
        contactBody.Put("last_name", lastName);
        contactBody.Put("email_ids", deliverable is null
            ? null
            : new JsonArray(new JsonObject { ["email_id"] = deliverable, ["is_primary"] = 1 }));
        contactBody.Put("phone_nos", phone is null
            ? null
            : new JsonArray(new JsonObject { ["phone"] = phone, ["is_primary_mobile_no"] = 1 }));
        contactBody.Put("designation", representative?.Position, dropWhenTooLong: true);
        contactBody.Put("is_primary_contact", 1);

        if (held.Count > 0)
        {
            return new ErpSupplierPayloadResult(null, string.Join(" ", held));
        }

        var userEmail = deliverable?.ToLowerInvariant();
        var user = userEmail is null
            ? null
            : new JsonObject
            {
                ["email"] = userEmail,
                ["first_name"] = firstName,
                ["user_type"] = WebsiteUserType,
                ["roles"] = new JsonArray(new JsonObject { ["role"] = SupplierRole }),
                ["send_welcome_email"] = 0,
            };

        return new ErpSupplierPayloadResult(
            new ErpSupplierPayload(
                supplierBody.Json,
                addressBody.Json,
                addressFields.Has("links"),
                contactBody.Json,
                contactFields.Has("links"),
                user,
                supplierName,
                Clean(legal?.TaxId),
                userEmail,
                notes),
            null);
    }

    public static string? ErpCountryName(string? portalCountry) =>
        string.IsNullOrWhiteSpace(portalCountry)
            ? null
            : ErpCountryNames.GetValueOrDefault(MinistrySupplierFeedCsv.CountryCode(portalCountry));

    private static string? GovernorateName(string regionCode)
    {
        foreach (var (code, names) in ErpAddressMapper.Governorates)
        {
            if (string.Equals(code, regionCode?.Trim(), StringComparison.OrdinalIgnoreCase)) return names[0];
        }

        return null;
    }

    private static (string? First, string? Last) SplitName(string? fullName)
    {
        var words = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return words.Length switch
        {
            0 => (null, null),
            1 => (words[0], null),
            _ => (string.Join(' ', words[..^1]), words[^1]),
        };
    }

    private static JsonObject WithLink(JsonObject template, bool linked, string erpSupplierName)
    {
        var body = (JsonObject)template.DeepClone();

        if (linked)
        {
            body["links"] = new JsonArray(
                new JsonObject { ["link_doctype"] = ErpRecordType.Supplier, ["link_name"] = erpSupplierName });
        }

        return body;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // One request body, filled only with the fields the ERP has and measured against what it stores.
    private sealed class Body(ErpRecordFields fields, List<string> held, List<string> notes)
    {
        public JsonObject Json { get; } = new();

        public void Put(string field, string? value, bool dropWhenTooLong = false)
        {
            var clean = Clean(value);
            if (clean is null || !Present(field)) return;

            var definition = fields[field]!;

            if (definition.MaxLength is { } max && clean.Length > max)
            {
                var measured = $"The ERP's {fields.RecordType} {field} holds at most {max} characters, and the portal's "
                    + $"value is {clean.Length}";

                if (dropWhenTooLong)
                {
                    notes.Add(measured + "; it is left out rather than cut.");
                }
                else
                {
                    held.Add(measured + "; it is held rather than cut.");
                }

                return;
            }

            if (definition.Type == "Select" && definition.Options.Count > 0 && !definition.Options.Contains(clean))
            {
                held.Add($"The ERP's {fields.RecordType} {field} offers {string.Join(", ", definition.Options)}, "
                    + $"not \"{clean}\".");
                return;
            }

            Json[field] = clean;
        }

        public void Put(string field, int value)
        {
            if (Present(field)) Json[field] = value;
        }

        public void Put(string field, JsonArray? rows)
        {
            if (rows is not null && Present(field)) Json[field] = rows;
        }

        private bool Present(string field)
        {
            if (fields.Has(field)) return true;

            notes.Add($"The ERP's {fields.RecordType} has no {field} field, so the portal's value for it is not sent.");
            return false;
        }
    }
}
