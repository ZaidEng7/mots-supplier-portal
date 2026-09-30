// What the ERP is sent to create a supplier that registered in the portal, one field at a time.
//
// ONE ASSERTION PER MAPPED FIELD AND PER OMISSION, so each test fails when its own line in ErpSupplierPayload is
// removed and no other. A test that compared whole bodies would fail for every change at once and say nothing about
// which mapping broke.
//
// THE ERP'S FIELD LISTS ARE THE TWO SERVERS WE KNOW. RealErp is the production shape: a naming series with one option,
// Seven Gates' custom fields, and Select fields with their options. TestErp has none of the custom fields and no naming
// series, as the test server has none, and the omission tests run against it: a field the ERP does not have is left
// out, and the note says so.
//
// HELD IS AN ANSWER, NOT AN EXCEPTION, and it names every reason at once, so an administrator fixes a supplier in one
// pass. Each held case is tried alone as well, so no reason hides behind another.
//
// THE CONTROL for the held cases is the plain supplier, which must not be held: a builder that held everything would
// pass every held test.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class ErpSupplierPayloadTests
{
    private const string Group = "Local Suppliers - SYP";
    private const string ErpName = "SUP-2026-00092";

    private static ErpFieldDefinition Data(string name, int? max = 140) => new(name, "Data", [], max);

    private static ErpFieldDefinition Select(string name, params string[] options) => new(name, "Select", options, 140);

    private static ErpFieldDefinition Link(string name, string target) => new(name, "Link", [target], 140);

    private static ErpFieldDefinition Other(string name, string type) => new(name, type, [], null);

    private static readonly ErpRecordFields RealSupplier = new(ErpRecordType.Supplier,
    [
        Select("naming_series", "SUP-.YYYY.-.#####"),
        Data("supplier_name"),
        Data(ErpSupplierPayload.ArabicNameField),
        Select("supplier_type", "Company", "Individual", "Partnership"),
        Link("supplier_group", "Supplier Group"),
        Link("country", "Country"),
        Link("default_currency", "Currency"),
        Data("tax_id"),
        Data(ErpSupplierPayload.RegistrationNumberField),
        Data(ErpSupplierPayload.RegistrationTypeField),
        Other("supplier_details", "Text"),
        Data("website"),
        Data("email_id"),
        Data("mobile_no"),
    ]);

    private static readonly ErpRecordFields TestSupplier = new(ErpRecordType.Supplier,
    [
        Data("supplier_name"),
        Select("supplier_type", "Company", "Individual", "Partnership"),
        Link("supplier_group", "Supplier Group"),
        Link("country", "Country"),
        Link("default_currency", "Currency"),
        Data("tax_id"),
    ]);

    private static readonly ErpRecordFields RealAddress = new(ErpRecordType.Address,
    [
        Data("address_title"),
        Select("address_type", "Billing", "Shipping", "Office"),
        Data("address_line1"),
        Data("address_line2"),
        Data("city"),
        Data("state"),
        Link("country", "Country"),
        Data("pincode"),
        Other("is_primary_address", "Check"),
        Other("links", "Table"),
    ]);

    private static readonly ErpRecordFields RealContact = new(ErpRecordType.Contact,
    [
        Data("first_name"),
        Data("last_name"),
        Other("email_ids", "Table"),
        Other("phone_nos", "Table"),
        Data("designation"),
        Other("is_primary_contact", "Check"),
        Other("links", "Table"),
    ]);

    private static readonly ErpRecordFields Bare = new(ErpRecordType.Address, [Data("address_line1"), Data("city")]);

    private static Supplier Registered(
        string legalNameEn = "Test Trading Co",
        string legalNameAr = "شركة الاختبار التجارية",
        SupplierLegalType legalType = SupplierLegalType.Company,
        string? taxId = "TAX-1",
        string? registrationNumber = "CR-1",
        string country = "SY",
        string representativeName = "Zaid Abdul Karim",
        string email = "Zaid@Example.com",
        string? website = "https://test.example",
        string? description = "A tourism supplier")
    {
        var supplier = Supplier.Register("SUP-2026-000092", "الاختبار", "Test Co", registrationNumber, representativeName, email);

        supplier.MarkEmailVerified();
        supplier.UpdateCoreProfile(description, website, "SME", "SYP");
        supplier.UpdateLegalInfo(legalNameAr, legalNameEn, registrationNumber, taxId, legalType, null, isComplianceCritical: false);
        supplier.AddAddress(AddressKind.HeadOffice, "12 Baghdad St", "Floor 3", "Damascus", "DIM", country, "11111", null, null);
        supplier.Representatives[0].Phone = "+963944000000";
        supplier.Representatives[0].Position = "Director";

        return supplier;
    }

    private static ErpSupplierPayloadResult Build(Supplier supplier, ErpRecordFields? supplierFields = null, string? group = Group) =>
        ErpSupplierPayload.Build(supplier, group, supplierFields ?? RealSupplier, RealAddress, RealContact);

    private static ErpSupplierPayload Ready(Supplier supplier, ErpRecordFields? supplierFields = null)
    {
        var result = Build(supplier, supplierFields);

        result.HeldReason.Should().BeNull();
        return result.Payload!;
    }

    private static JsonObject SupplierBody(Supplier supplier, ErpRecordFields? fields = null) => Ready(supplier, fields).Supplier();

    private static JsonObject AddressBody(Supplier supplier) => Ready(supplier).Address(ErpName);

    private static JsonObject ContactBody(Supplier supplier) => Ready(supplier).Contact(ErpName);

    private static string? Text(JsonObject body, string field) => body[field]?.GetValue<string>();

    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Json(JsonNode? node) => node!.ToJsonString(Readable);

    [Fact]
    public void The_naming_series_is_the_erp_fields_first_option()
    {
        Text(SupplierBody(Registered()), "naming_series").Should().Be("SUP-.YYYY.-.#####");
    }

    [Fact]
    public void The_supplier_name_is_the_english_legal_name()
    {
        Text(SupplierBody(Registered(legalNameEn: "Test Trading Co")), "supplier_name").Should().Be("Test Trading Co");
    }

    [Fact]
    public void The_supplier_name_falls_back_to_the_display_name_when_the_legal_name_is_blank()
    {
        Text(SupplierBody(Registered(legalNameEn: "  ")), "supplier_name").Should().Be("Test Co");
    }

    [Fact]
    public void The_arabic_legal_name_goes_to_the_field_the_import_reads()
    {
        Text(SupplierBody(Registered()), "custom_supplier_arabic_name").Should().Be("شركة الاختبار التجارية");
    }

    [Theory]
    [InlineData(SupplierLegalType.Company, "Company")]
    [InlineData(SupplierLegalType.Individual, "Individual")]
    [InlineData(SupplierLegalType.Partnership, "Partnership")]
    public void The_supplier_type_is_the_legal_type(SupplierLegalType legalType, string expected)
    {
        Text(SupplierBody(Registered(legalType: legalType)), "supplier_type").Should().Be(expected);
    }

    [Fact]
    public void The_supplier_group_is_the_connections_default_group()
    {
        Text(SupplierBody(Registered()), "supplier_group").Should().Be(Group);
    }

    [Theory]
    [InlineData("SY")]
    [InlineData("syria")]
    [InlineData("Syrian Arab Republic")]
    [InlineData("سوريا")]
    public void The_country_is_the_erps_name_for_syria(string portalCountry)
    {
        Text(SupplierBody(Registered(country: portalCountry)), "country").Should().Be("Syria");
    }

    [Fact]
    public void The_currency_is_sent_as_stored()
    {
        Text(SupplierBody(Registered()), "default_currency").Should().Be("SYP");
    }

    [Fact]
    public void The_tax_number_is_sent_as_stored()
    {
        Text(SupplierBody(Registered(taxId: "TAX-77")), "tax_id").Should().Be("TAX-77");
    }

    [Fact]
    public void The_registration_number_goes_to_the_field_the_import_reads()
    {
        Text(SupplierBody(Registered(registrationNumber: "CR-55")), "custom_registration_number").Should().Be("CR-55");
    }

    [Fact]
    public void The_registration_type_goes_to_the_field_the_import_reads()
    {
        var supplier = Supplier.ImportFromErp(
            "SUP-2026-000093", "SUP-2026-00093", "Imported Co", null, SupplierLegalType.Company, "SYP",
            "Imported Co", "sales@imported.example", null,
            details: new ErpSupplierDetails("المستورد", "73260", "Commercial", null, null));
        supplier.AddAddress(AddressKind.HeadOffice, "1 Street", null, "Damascus", "DIM", "Syria", null, null, null);

        Text(SupplierBody(supplier), "custom_registration_type").Should().Be("Commercial");
    }

    [Fact]
    public void The_description_goes_to_the_supplier_details()
    {
        Text(SupplierBody(Registered(description: "Hotel linen")), "supplier_details").Should().Be("Hotel linen");
    }

    [Fact]
    public void The_website_is_sent_as_stored()
    {
        Text(SupplierBody(Registered(website: "https://linen.example")), "website").Should().Be("https://linen.example");
    }

    [Fact]
    public void The_suppliers_email_and_mobile_are_not_sent_on_the_supplier_although_the_erp_has_the_fields()
    {
        SupplierBody(Registered()).Should().NotContainKeys("email_id", "mobile_no");
    }

    [Fact]
    public void An_empty_value_is_left_out()
    {
        SupplierBody(Registered(taxId: null)).Should().NotContainKey("tax_id");
    }

    [Fact]
    public void A_value_padded_with_spaces_is_trimmed()
    {
        Text(SupplierBody(Registered(taxId: "  TAX-9  ")), "tax_id").Should().Be("TAX-9");
    }

    [Fact]
    public void The_naming_series_is_left_out_where_the_erp_has_no_such_field()
    {
        SupplierBody(Registered(), TestSupplier).Should().NotContainKey("naming_series");
    }

    [Fact]
    public void The_arabic_name_is_left_out_where_the_erp_has_no_such_field()
    {
        SupplierBody(Registered(), TestSupplier).Should().NotContainKey("custom_supplier_arabic_name");
    }

    [Fact]
    public void The_registration_number_is_left_out_where_the_erp_has_no_such_field()
    {
        SupplierBody(Registered(), TestSupplier).Should().NotContainKey("custom_registration_number");
    }

    [Fact]
    public void A_value_the_erp_has_no_field_for_is_named_in_the_notes()
    {
        Ready(Registered(), TestSupplier).Notes.Should().Contain(
            "The ERP's Supplier has no custom_supplier_arabic_name field, so the portal's value for it is not sent.");
    }

    [Fact]
    public void A_website_too_long_for_the_erp_is_left_out_with_a_note_and_the_push_is_not_held()
    {
        var payload = Ready(Registered(website: "https://" + new string('w', 140) + ".example"));

        payload.Supplier().Should().NotContainKey("website");
        payload.Notes.Should().Contain(note => note.StartsWith("The ERP's Supplier website holds at most 140 characters"));
    }

    [Fact]
    public void The_address_title_is_the_supplier_name()
    {
        Text(AddressBody(Registered(legalNameEn: "Test Trading Co")), "address_title").Should().Be("Test Trading Co");
    }

    [Fact]
    public void The_address_type_is_billing()
    {
        Text(AddressBody(Registered()), "address_type").Should().Be("Billing");
    }

    [Fact]
    public void The_first_street_line_is_sent()
    {
        Text(AddressBody(Registered()), "address_line1").Should().Be("12 Baghdad St");
    }

    [Fact]
    public void The_second_street_line_is_sent()
    {
        Text(AddressBody(Registered()), "address_line2").Should().Be("Floor 3");
    }

    [Fact]
    public void The_city_is_sent()
    {
        Text(AddressBody(Registered()), "city").Should().Be("Damascus");
    }

    [Fact]
    public void The_state_is_the_governorates_arabic_name()
    {
        Text(AddressBody(Registered()), "state").Should().Be("دمشق");
    }

    [Fact]
    public void The_address_country_is_the_erps_name()
    {
        Text(AddressBody(Registered()), "country").Should().Be("Syria");
    }

    [Fact]
    public void The_postal_code_goes_to_the_pincode()
    {
        Text(AddressBody(Registered()), "pincode").Should().Be("11111");
    }

    [Fact]
    public void The_address_is_marked_primary()
    {
        AddressBody(Registered())["is_primary_address"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void The_address_links_to_the_erp_supplier()
    {
        Json(AddressBody(Registered())["links"]).Should().Be(
            """[{"link_doctype":"Supplier","link_name":"SUP-2026-00092"}]""");
    }

    [Fact]
    public void The_primary_address_is_the_one_sent()
    {
        var supplier = Registered();
        var first = supplier.Addresses[0];
        supplier.AddAddress(AddressKind.Branch, "7 Hamra St", null, "Homs", "HOM", "Syria", null, null, null);
        supplier.RemoveAddress(first.Id);

        Text(AddressBody(supplier), "address_line1").Should().Be("7 Hamra St");
    }

    [Fact]
    public void Fields_the_erps_address_does_not_have_are_left_out()
    {
        var address = ErpSupplierPayload.Build(Registered(), Group, RealSupplier, Bare, RealContact).Payload!.Address(ErpName);

        address.Should().NotContainKeys("state", "links", "pincode", "address_title");
    }

    [Fact]
    public void The_contacts_first_name_is_everything_before_the_last_space()
    {
        Text(ContactBody(Registered(representativeName: "Zaid Abdul Karim")), "first_name").Should().Be("Zaid Abdul");
    }

    [Fact]
    public void The_contacts_last_name_is_the_last_word()
    {
        Text(ContactBody(Registered(representativeName: "Zaid Abdul Karim")), "last_name").Should().Be("Karim");
    }

    [Fact]
    public void A_one_word_name_has_no_last_name()
    {
        ContactBody(Registered(representativeName: "Zaid")).Should().NotContainKey("last_name");
    }

    [Fact]
    public void The_email_is_the_contacts_primary_email()
    {
        Json(ContactBody(Registered(email: "Zaid@Example.com"))["email_ids"]).Should().Be(
            """[{"email_id":"Zaid@Example.com","is_primary":1}]""");
    }

    [Fact]
    public void The_phone_is_the_contacts_primary_mobile()
    {
        Json(ContactBody(Registered())["phone_nos"]).Should().Be(
            """[{"phone":"+963944000000","is_primary_mobile_no":1}]""");
    }

    [Fact]
    public void A_contact_with_no_phone_sends_no_phone_rows()
    {
        var supplier = Registered();
        supplier.Representatives[0].Phone = null;

        ContactBody(supplier).Should().NotContainKey("phone_nos");
    }

    [Fact]
    public void The_position_is_the_contacts_designation()
    {
        Text(ContactBody(Registered()), "designation").Should().Be("Director");
    }

    [Fact]
    public void The_contact_is_marked_primary()
    {
        ContactBody(Registered())["is_primary_contact"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void The_contact_links_to_the_erp_supplier()
    {
        Json(ContactBody(Registered())["links"]).Should().Be(
            """[{"link_doctype":"Supplier","link_name":"SUP-2026-00092"}]""");
    }

    [Fact]
    public void The_user_is_the_colleagues_call_exactly_with_no_password()
    {
        Json(Ready(Registered(email: " Zaid@Example.com ")).User()).Should().Be(
            """{"email":"zaid@example.com","first_name":"Zaid Abdul","user_type":"Website User","roles":[{"role":"Supplier"}],"send_welcome_email":0}""");
    }

    [Fact]
    public void The_user_email_is_what_the_portal_users_and_the_contact_will_name()
    {
        Ready(Registered(email: "Zaid@Example.com")).UserEmail.Should().Be("zaid@example.com");
    }

    [Fact]
    public void A_placeholder_email_makes_no_user()
    {
        Ready(Registered(email: "homs-linen-1a2b3c@erp-import.invalid")).User().Should().BeNull();
    }

    [Fact]
    public void A_placeholder_email_is_not_put_on_the_contact()
    {
        ContactBody(Registered(email: "homs-linen-1a2b3c@erp-import.invalid")).Should().NotContainKey("email_ids");
    }

    [Fact]
    public void The_supplier_name_and_tax_number_are_kept_for_the_lookups_before_a_create()
    {
        var payload = Ready(Registered(legalNameEn: "Test Trading Co", taxId: "TAX-1"));

        payload.SupplierName.Should().Be("Test Trading Co");
        payload.TaxId.Should().Be("TAX-1");
    }

    [Fact]
    public void Each_body_is_a_fresh_copy_so_one_call_cannot_change_the_next()
    {
        var payload = Ready(Registered());
        payload.Supplier()["supplier_name"] = "changed";

        Text(payload.Supplier(), "supplier_name").Should().Be("Test Trading Co");
    }

    [Fact]
    public void The_plain_supplier_is_not_held()
    {
        Build(Registered()).IsHeld.Should().BeFalse();
    }

    [Fact]
    public void No_default_group_holds_the_push()
    {
        Build(Registered(), group: " ").HeldReason.Should().Be(
            "No default ERP supplier group is set on the ERP connection, and the ERP needs one for every supplier.");
    }

    [Fact]
    public void A_country_with_no_erp_name_holds_the_push_and_names_it()
    {
        Build(Registered(country: "Lebanon")).HeldReason.Should().Contain("\"Lebanon\"").And.Contain("only Syria is mapped");
    }

    [Fact]
    public void A_supplier_name_longer_than_the_erp_stores_holds_the_push_rather_than_being_cut()
    {
        Build(Registered(legalNameEn: new string('n', 141))).HeldReason.Should().StartWith(
            "The ERP's Supplier supplier_name holds at most 140 characters, and the portal's value is 141; it is held "
            + "rather than cut.");
    }

    [Fact]
    public void A_supplier_name_of_exactly_the_erps_length_is_sent()
    {
        Build(Registered(legalNameEn: new string('n', 140))).IsHeld.Should().BeFalse();
    }

    [Fact]
    public void A_value_the_erps_select_does_not_offer_holds_the_push()
    {
        var withoutPartnership = new ErpRecordFields(ErpRecordType.Supplier,
            [Data("supplier_name"), Select("supplier_type", "Company", "Individual")]);

        Build(Registered(legalType: SupplierLegalType.Partnership), withoutPartnership).HeldReason.Should().Be(
            "The ERP's Supplier supplier_type offers Company, Individual, not \"Partnership\".");
    }

    [Fact]
    public void A_supplier_with_no_address_is_held()
    {
        var supplier = Registered();
        supplier.RemoveAddress(supplier.Addresses[0].Id);

        Build(supplier).HeldReason.Should().Be("The supplier has no address to create in the ERP.");
    }

    [Fact]
    public void Every_reason_is_named_at_once()
    {
        var result = Build(Registered(country: "Lebanon"), group: null);

        result.HeldReason.Should().Contain("No default ERP supplier group").And.Contain("\"Lebanon\"");
        result.Payload.Should().BeNull();
    }
}
