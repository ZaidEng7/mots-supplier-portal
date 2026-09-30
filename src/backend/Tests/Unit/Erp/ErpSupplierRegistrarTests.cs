// What the writer sends to the ERP, and what it makes of the answers.
//
// EACH REQUEST IS RECORDED AS IT WAS SENT: the method, the path with its record type and name encoded, the credential
// with its lower-case "token" scheme, and the body read inside the handler. The body has to be read there, because the
// writer disposes the request as soon as the answer arrives, and a test that read it afterwards would read nothing.
//
// THE BODIES ARE COMPARED EXACTLY. The writer passes ErpSupplierPayload's bodies through untouched, and the one body it
// builds itself - the portal users - is the one where a missing row is a deleted row in the ERP.
//
// THE PORTAL USERS ARE READ BEFORE THEY ARE REPLACED, and the control is the supplier that already lists the user: it
// must get no PUT at all. A writer that always put would pass the "keeps the existing rows" test and still send a
// duplicate on every resumed push.
//
// THE WRITE SWITCH IS TRIED ON EVERY WRITE, with the requests counted rather than the exception alone: the owner's rule
// is that nothing is sent while it is off, and a writer that threw after sending would pass a test that only caught the
// exception. Reads run with it off, which is the control.
//
// THE FAILURE TESTS DRIVE REAL ANSWERS THROUGH THE WRITER, one per kind the push acts on, including the two that never
// reach an answer: a timeout and a refused connection on a create, both of which leave the outcome unknown. The caller
// cancelling is the control there, and is not the ERP's failure at all.
//
// NOTHING HERE TOUCHES THE NETWORK.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpSupplierRegistrarTests
{
    private const string ErpName = "SUP-2026-00092";

    private sealed record Sent(HttpMethod Method, Uri Uri, string? Scheme, string? Parameter, string? Body)
    {
        public string Query => Uri.UnescapeDataString(Uri.Query);

        public JsonNode? Filters => JsonNode.Parse(Uri.UnescapeDataString(
            Uri.Query.TrimStart('?').Split('&').Single(part => part.StartsWith("filters=", StringComparison.Ordinal))["filters=".Length..]));
    }

    private sealed class RoutedHandler(Func<Sent, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var sent = new Sent(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));

            Requests.Add(sent);
            return answer(sent);
        }
    }

    private sealed class FixedConnection(ErpConnection? connection) : IErpConnectionProvider
    {
        public Task<ErpConnection?> CurrentAsync(CancellationToken ct) => Task.FromResult(connection);
    }

    private static ErpConnection Connection(bool writes = true) =>
        new("http://erp.example", "key", "secret", IsEnabled: true, ErpConnectionSource.Database, CreateSuppliersInErp: writes);

    private static (ErpSupplierRegistrar Registrar, RoutedHandler Handler) Answering(
        Func<Sent, HttpResponseMessage> answer, ErpConnection? connection = null)
    {
        var handler = new RoutedHandler(answer);
        var options = Options.Create(new ErpOptions
        {
            BaseUrl = "http://erp.example", ApiKey = "key", ApiSecret = "secret", Company = "Seven Gates",
        });

        return (new ErpSupplierRegistrar(new HttpClient(handler), new FixedConnection(connection ?? Connection()), options),
            handler);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Created(string name = ErpName) =>
        Json(new JsonObject { ["data"] = new JsonObject { ["name"] = name } }.ToJsonString());

    private static JsonObject Body(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public async Task A_supplier_is_created_with_a_post_of_the_exact_body_and_its_erp_name_comes_back()
    {
        var (registrar, handler) = Answering(_ => Created());

        var name = await registrar.CreateSupplierAsync(
            Body("""{"supplier_name":"Test Trading Co","supplier_group":"Local"}"""), CancellationToken.None);

        name.Should().Be(ErpName, "the ERP mints the name from its naming series, and the push saves it as ExternalId");
        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Uri.AbsoluteUri.Should().Be("http://erp.example/api/resource/Supplier");
        sent.Body.Should().Be("""{"supplier_name":"Test Trading Co","supplier_group":"Local"}""");
    }

    [Fact]
    public async Task A_body_is_sent_as_readable_utf8_with_arabic_and_a_plus_as_they_are()
    {
        var (registrar, handler) = Answering(_ => Created());

        await registrar.CreateContactAsync(
            new JsonObject { ["first_name"] = "زيد", ["phone"] = "+963944000000" }, CancellationToken.None);

        handler.Requests[0].Body.Should().Be("""{"first_name":"زيد","phone":"+963944000000"}""",
            "a request somebody reads in a log should say what it sent, not a row of \\u escapes");
    }

    [Fact]
    public async Task Every_request_carries_the_lower_case_token_scheme()
    {
        var (registrar, handler) = Answering(_ => Created());

        await registrar.CreateSupplierAsync(Body("{}"), CancellationToken.None);

        handler.Requests[0].Scheme.Should().Be("token",
            "the ERP compares the scheme case-sensitively, and a capitalised one answers 403 with an empty body");
        handler.Requests[0].Parameter.Should().Be("key:secret");
    }

    [Fact]
    public async Task An_address_is_created_on_the_address_resource()
    {
        var (registrar, handler) = Answering(_ => Created("Test Trading Co-Billing"));

        var name = await registrar.CreateAddressAsync(Body("""{"address_line1":"12 Baghdad St"}"""), CancellationToken.None);

        name.Should().Be("Test Trading Co-Billing");
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].Uri.AbsolutePath.Should().Be("/api/resource/Address");
        handler.Requests[0].Body.Should().Be("""{"address_line1":"12 Baghdad St"}""");
    }

    [Fact]
    public async Task A_contact_is_created_on_the_contact_resource()
    {
        var (registrar, handler) = Answering(_ => Created("Zaid Abdul Karim-Test Trading Co"));

        var name = await registrar.CreateContactAsync(Body("""{"first_name":"Zaid Abdul"}"""), CancellationToken.None);

        name.Should().Be("Zaid Abdul Karim-Test Trading Co");
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].Uri.AbsolutePath.Should().Be("/api/resource/Contact");
        handler.Requests[0].Body.Should().Be("""{"first_name":"Zaid Abdul"}""");
    }

    [Fact]
    public async Task A_user_is_created_on_the_user_resource()
    {
        var (registrar, handler) = Answering(_ => Created("zaid@example.com"));

        var name = await registrar.CreateUserAsync(Body("""{"email":"zaid@example.com"}"""), CancellationToken.None);

        name.Should().Be("zaid@example.com");
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].Uri.AbsolutePath.Should().Be("/api/resource/User");
        handler.Requests[0].Body.Should().Be("""{"email":"zaid@example.com"}""");
    }

    [Fact]
    public async Task A_portal_user_is_added_by_reading_the_supplier_and_putting_back_every_row_with_the_new_one()
    {
        var (registrar, handler) = Answering(sent => sent.Method == HttpMethod.Get
            ? Json("""
                {"data": {"name": "SUP-2026-00092", "supplier_name": "Test Trading Co", "portal_users": [
                  {"name": "a1b2c3", "user": "finance@test.example", "idx": 1, "doctype": "Portal User"}]}}
                """)
            : Created());

        await registrar.AddPortalUserAsync(ErpName, "zaid@example.com", CancellationToken.None);

        handler.Requests.Select(r => (r.Method, r.Uri.AbsolutePath)).Should().Equal(
            (HttpMethod.Get, "/api/resource/Supplier/SUP-2026-00092"),
            (HttpMethod.Put, "/api/resource/Supplier/SUP-2026-00092"));
        handler.Requests[1].Body.Should().Be(
            """{"portal_users":[{"name":"a1b2c3","user":"finance@test.example","idx":1,"doctype":"Portal User"},{"user":"zaid@example.com"}]}""",
            "a PUT replaces the whole child table, so a row left out is a row the ERP deletes");
    }

    [Fact]
    public async Task A_supplier_with_no_portal_users_gets_a_table_of_one()
    {
        var (registrar, handler) = Answering(sent => sent.Method == HttpMethod.Get
            ? Json("""{"data": {"name": "SUP-2026-00092"}}""")
            : Created());

        await registrar.AddPortalUserAsync(ErpName, "zaid@example.com", CancellationToken.None);

        handler.Requests[1].Body.Should().Be("""{"portal_users":[{"user":"zaid@example.com"}]}""");
    }

    [Fact]
    public async Task A_user_already_among_the_portal_users_is_not_put_again()
    {
        var (registrar, handler) = Answering(_ => Json("""
            {"data": {"name": "SUP-2026-00092", "portal_users": [{"name": "a1b2c3", "user": "Zaid@Example.com"}]}}
            """));

        await registrar.AddPortalUserAsync(ErpName, "zaid@example.com", CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task A_record_name_with_spaces_and_brackets_is_percent_encoded_in_the_path()
    {
        var (registrar, handler) = Answering(_ => Json("""{"data": {"portal_users": []}}"""));

        await registrar.AddPortalUserAsync("Damascus Supplies Co (seed)", "zaid@example.com", CancellationToken.None);

        handler.Requests[0].Uri.AbsolutePath.Should().Be(
            "/api/resource/Supplier/Damascus%20Supplies%20Co%20%28seed%29",
            "the test server names suppliers by their name, and a space encoded as '+' answers 404 in a path");
    }

    [Fact]
    public async Task The_contacts_user_is_set_with_a_put_of_the_user_alone()
    {
        var (registrar, handler) = Answering(_ => Created("Zaid Abdul Karim-Test Trading Co"));

        await registrar.SetContactUserAsync("Zaid Abdul Karim-Test Trading Co", "zaid@example.com", CancellationToken.None);

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Put);
        sent.Uri.AbsolutePath.Should().Be("/api/resource/Contact/Zaid%20Abdul%20Karim-Test%20Trading%20Co");
        sent.Body.Should().Be("""{"user":"zaid@example.com"}""");
    }

    private const string SupplierMeta = """
    {"docs": [
      {"name": "Supplier", "fields": [
        {"fieldname": "naming_series", "fieldtype": "Select", "options": "SUP-.YYYY.-.#####", "length": 0},
        {"fieldname": "supplier_name", "fieldtype": "Data", "options": null, "length": 0},
        {"fieldname": "custom_supplier_arabic_name", "fieldtype": "Data", "length": 0},
        {"fieldname": "supplier_group", "fieldtype": "Link", "options": "Supplier Group"},
        {"fieldname": "supplier_type", "fieldtype": "Select", "options": "Company\nIndividual\nPartnership"},
        {"fieldname": "default_currency", "fieldtype": "Link", "options": "Currency", "length": 3},
        {"fieldname": "supplier_details", "fieldtype": "Text"},
        {"fieldtype": "Section Break"}
      ]},
      {"name": "Portal User", "fields": [{"fieldname": "user", "fieldtype": "Link", "options": "User"}]}
    ], "user_settings": "{}"}
    """;

    [Fact]
    public async Task The_field_list_is_read_from_the_erps_own_form_method()
    {
        var (registrar, handler) = Answering(_ => Json(SupplierMeta));

        await registrar.ReadFieldsAsync(ErpRecordType.Supplier, CancellationToken.None);

        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].Uri.AbsolutePath.Should().Be("/api/method/frappe.desk.form.load.getdoctype");
        handler.Requests[0].Uri.Query.Should().Be("?doctype=Supplier");
    }

    [Fact]
    public async Task The_field_list_holds_the_record_types_own_fields_and_not_its_child_tables()
    {
        var (registrar, _) = Answering(_ => Json(SupplierMeta));

        var fields = await registrar.ReadFieldsAsync(ErpRecordType.Supplier, CancellationToken.None);

        fields.Names.Should().BeEquivalentTo(
            "naming_series", "supplier_name", "custom_supplier_arabic_name", "supplier_group", "supplier_type",
            "default_currency", "supplier_details");
    }

    [Fact]
    public async Task A_select_fields_options_are_read_one_per_line()
    {
        var (registrar, _) = Answering(_ => Json(SupplierMeta));

        var fields = await registrar.ReadFieldsAsync(ErpRecordType.Supplier, CancellationToken.None);

        fields["supplier_type"]!.Options.Should().Equal("Company", "Individual", "Partnership");
        fields["naming_series"]!.Options.Should().Equal("SUP-.YYYY.-.#####");
    }

    [Fact]
    public async Task A_text_field_the_erp_keeps_at_its_default_length_is_measured_at_140()
    {
        var (registrar, _) = Answering(_ => Json(SupplierMeta));

        var fields = await registrar.ReadFieldsAsync(ErpRecordType.Supplier, CancellationToken.None);

        fields["supplier_name"]!.MaxLength.Should().Be(140);
        fields["supplier_group"]!.MaxLength.Should().Be(140);
    }

    [Fact]
    public async Task A_stated_length_wins_and_a_long_text_field_has_none()
    {
        var (registrar, _) = Answering(_ => Json(SupplierMeta));

        var fields = await registrar.ReadFieldsAsync(ErpRecordType.Supplier, CancellationToken.None);

        fields["default_currency"]!.MaxLength.Should().Be(3);
        fields["supplier_details"]!.MaxLength.Should().BeNull();
    }

    [Fact]
    public async Task A_record_type_with_a_space_is_sent_as_one_query_value()
    {
        var (registrar, handler) = Answering(_ => Json("""{"docs": [{"name": "Dynamic Link", "fields": []}]}"""));

        await registrar.ReadFieldsAsync("Dynamic Link", CancellationToken.None);

        handler.Requests[0].Uri.Query.Should().Be("?doctype=Dynamic%20Link");
    }

    [Fact]
    public async Task Suppliers_are_found_by_their_tax_number()
    {
        var (registrar, handler) = Answering(_ => Json("""
            {"data": [{"name": "SUP-2026-00017", "supplier_name": "Test Trading Co", "tax_id": "TAX-1"}]}
            """));

        var matches = await registrar.FindSuppliersByTaxIdAsync("TAX-1", CancellationToken.None);

        matches.Should().Equal(new ErpSupplierMatch("SUP-2026-00017", "Test Trading Co", "TAX-1"));
        handler.Requests[0].Uri.AbsolutePath.Should().Be("/api/resource/Supplier");
        handler.Requests[0].Query.Should().Contain("""filters=[["tax_id","=","TAX-1"]]""");
        handler.Requests[0].Query.Should().Contain("""fields=["name","supplier_name","tax_id"]""");
    }

    [Fact]
    public async Task The_portals_own_creates_are_found_by_name_by_the_api_user_and_since_a_time_on_the_erps_clock()
    {
        var (registrar, handler) = Answering(sent => sent.Uri.AbsolutePath.StartsWith("/api/method/")
            ? Json("""{"message": "portal@7gates.example"}""")
            : Json("""{"data": [{"name": "SUP-2026-00092", "supplier_name": "Test Trading Co", "tax_id": null}]}"""));

        var matches = await registrar.FindSuppliersCreatedByPortalAsync(
            "Test Trading Co", new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero), CancellationToken.None);

        matches.Should().Equal(new ErpSupplierMatch("SUP-2026-00092", "Test Trading Co", null));
        handler.Requests[0].Uri.AbsolutePath.Should().Be("/api/method/frappe.auth.get_logged_user");
        JsonNode.DeepEquals(
                handler.Requests[1].Filters,
                JsonNode.Parse("""
                    [["supplier_name","=","Test Trading Co"],["owner","=","portal@7gates.example"],
                     ["creation",">=","2026-09-30 12:00:00"]]
                    """))
            .Should().BeTrue("the ERP stores creation in Damascus time with no offset, and 09:00 UTC is 12:00 there");
    }

    [Fact]
    public async Task The_addresses_already_linked_to_a_supplier_are_listed_by_that_link()
    {
        var (registrar, handler) = Answering(_ => Json("""
            {"data": [{"name": "Test Trading Co-Billing", "address_type": "Billing", "address_line1": "12 Baghdad St",
                       "city": "Damascus"}]}
            """));

        var addresses = await registrar.ListLinkedAddressesAsync(ErpName, CancellationToken.None);

        addresses.Should().Equal(new ErpLinkedAddress("Test Trading Co-Billing", "Billing", "12 Baghdad St", "Damascus"));
        handler.Requests[0].Uri.AbsolutePath.Should().Be("/api/resource/Address");
        handler.Requests[0].Query.Should().Contain(
            """[["Dynamic Link","link_doctype","=","Supplier"],["Dynamic Link","link_name","=","SUP-2026-00092"]]""");
    }

    [Fact]
    public async Task The_contacts_already_linked_to_a_supplier_are_listed_with_their_email_and_user()
    {
        var (registrar, handler) = Answering(_ => Json("""
            {"data": [{"name": "Zaid-Test Trading Co", "email_id": "zaid@example.com", "user": null}]}
            """));

        var contacts = await registrar.ListLinkedContactsAsync(ErpName, CancellationToken.None);

        contacts.Should().Equal(new ErpLinkedContact("Zaid-Test Trading Co", "zaid@example.com", null));
        handler.Requests[0].Uri.AbsolutePath.Should().Be("/api/resource/Contact");
        handler.Requests[0].Query.Should().Contain("""["Dynamic Link","link_name","=","SUP-2026-00092"]""");
    }

    [Fact]
    public async Task A_user_is_found_by_email()
    {
        var (registrar, handler) = Answering(_ => Json("""{"data": [{"name": "zaid@example.com"}]}"""));

        var user = await registrar.FindUserAsync("zaid@example.com", CancellationToken.None);

        user.Should().Be("zaid@example.com");
        handler.Requests[0].Uri.AbsolutePath.Should().Be("/api/resource/User");
        handler.Requests[0].Query.Should().Contain("""filters=[["email","=","zaid@example.com"]]""");
    }

    [Fact]
    public async Task No_user_with_that_email_is_none()
    {
        var (registrar, _) = Answering(_ => Json("""{"data": []}"""));

        (await registrar.FindUserAsync("nobody@example.com", CancellationToken.None)).Should().BeNull();
    }

    public static TheoryData<string> Writes() =>
        ["supplier", "address", "contact", "user", "portal user", "contact user"];

    private static Task Write(IErpSupplierRegistrar registrar, string write) => write switch
    {
        "supplier" => registrar.CreateSupplierAsync(Body("{}"), CancellationToken.None),
        "address" => registrar.CreateAddressAsync(Body("{}"), CancellationToken.None),
        "contact" => registrar.CreateContactAsync(Body("{}"), CancellationToken.None),
        "user" => registrar.CreateUserAsync(Body("{}"), CancellationToken.None),
        "portal user" => registrar.AddPortalUserAsync(ErpName, "zaid@example.com", CancellationToken.None),
        "contact user" => registrar.SetContactUserAsync("Zaid-Test Trading Co", "zaid@example.com", CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(write), write, null),
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Nothing_is_sent_while_writing_to_the_erp_is_switched_off(string write)
    {
        var (registrar, handler) = Answering(_ => Created(), Connection(writes: false));

        var act = () => Write(registrar, write);

        await act.Should().ThrowAsync<ErpWritesOffException>();
        handler.Requests.Should().BeEmpty("the switch gates every write, before any request exists");
    }

    [Fact]
    public async Task Reads_run_while_writing_is_switched_off()
    {
        var (registrar, handler) = Answering(_ => Json(SupplierMeta), Connection(writes: false));

        await registrar.ReadFieldsAsync(ErpRecordType.Supplier, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task No_connection_is_reported_as_not_configured()
    {
        var handler = new RoutedHandler(_ => Created());
        var registrar = new ErpSupplierRegistrar(
            new HttpClient(handler), new FixedConnection(null), Options.Create(new ErpOptions
            {
                BaseUrl = string.Empty, ApiKey = string.Empty, ApiSecret = string.Empty, Company = string.Empty,
            }));

        var act = () => registrar.CreateSupplierAsync(Body("{}"), CancellationToken.None);

        await act.Should().ThrowAsync<ErpNotConfiguredException>();
        handler.Requests.Should().BeEmpty();
    }

    private const string MissingGroup = """
    {"exc_type": "MandatoryError", "exception": "frappe.exceptions.MandatoryError: [Supplier, new-supplier-1]: supplier_group",
     "_server_messages": "[\"{\\\"message\\\": \\\"Error: Value missing for Supplier: <strong>Supplier Group</strong>\\\", \\\"title\\\": \\\"Message\\\", \\\"indicator\\\": \\\"red\\\", \\\"raise_exception\\\": 1}\"]"}
    """;

    private static async Task<ErpRequestException> RefusedCreate(Func<Sent, HttpResponseMessage> answer)
    {
        var (registrar, _) = Answering(answer);

        var act = () => registrar.CreateSupplierAsync(Body("{}"), CancellationToken.None);

        return (await act.Should().ThrowAsync<ErpRequestException>()).Which;
    }

    [Fact]
    public async Task A_refused_credential_on_a_create_pauses_the_push()
    {
        var refused = await RefusedCreate(_ => Json(
            """{"exc_type": "PermissionError", "exception": "frappe.exceptions.PermissionError: Insufficient Permission for Supplier"}""",
            HttpStatusCode.Forbidden));

        refused.PushKind.Should().Be(ErpPushFailureKind.Permission);
    }

    [Fact]
    public async Task A_broken_rule_is_permanent_and_carries_the_erps_own_sentence()
    {
        var refused = await RefusedCreate(_ => Json(MissingGroup, HttpStatusCode.ExpectationFailed));

        refused.PushKind.Should().Be(ErpPushFailureKind.Permanent);
        refused.ErpMessage.Should().Be("Error: Value missing for Supplier: Supplier Group",
            "the message is unwrapped from JSON inside a string inside JSON, and its HTML is stripped");
        refused.Message.Should().Be(
            "The ERP refused the Supplier create with 417 ExpectationFailed (MandatoryError): "
            + "Error: Value missing for Supplier: Supplier Group");
    }

    [Fact]
    public async Task Without_server_messages_the_exception_line_is_the_erps_message()
    {
        var refused = await RefusedCreate(_ => Json(
            """{"exc_type": "PermissionError", "exception": "frappe.exceptions.PermissionError: Insufficient Permission for Supplier"}""",
            HttpStatusCode.Forbidden));

        refused.ErpMessage.Should().Be("frappe.exceptions.PermissionError: Insufficient Permission for Supplier");
    }

    [Fact]
    public async Task A_missing_record_type_is_permanent()
    {
        var refused = await RefusedCreate(_ => Json("""{"exc_type": "DoesNotExistError"}""", HttpStatusCode.NotFound));

        refused.PushKind.Should().Be(ErpPushFailureKind.Permanent);
    }

    [Fact]
    public async Task A_conflict_on_a_create_means_the_record_already_exists()
    {
        var refused = await RefusedCreate(_ => Json("""{"exc_type": "DuplicateEntryError"}""", HttpStatusCode.Conflict));

        refused.PushKind.Should().Be(ErpPushFailureKind.AlreadyExists);
    }

    [Fact]
    public async Task A_unique_field_clash_on_a_create_means_the_record_already_exists_although_it_answers_417()
    {
        var refused = await RefusedCreate(_ => Json(
            """{"exc_type": "UniqueValidationError"}""", HttpStatusCode.ExpectationFailed));

        refused.PushKind.Should().Be(ErpPushFailureKind.AlreadyExists);
    }

    [Fact]
    public async Task A_timeout_on_a_create_leaves_the_outcome_unknown()
    {
        var refused = await RefusedCreate(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing."));

        refused.PushKind.Should().Be(ErpPushFailureKind.OutcomeUnknown,
            "the ERP may have made the supplier before the answer was lost, and it makes a second one for a second POST");
        refused.Message.Should().Contain("could not be reached");
    }

    [Fact]
    public async Task A_refused_connection_on_a_create_leaves_the_outcome_unknown()
    {
        var refused = await RefusedCreate(_ => throw new HttpRequestException("Connection refused"));

        refused.PushKind.Should().Be(ErpPushFailureKind.OutcomeUnknown);
    }

    [Fact]
    public async Task A_bad_gateway_on_a_create_leaves_the_outcome_unknown()
    {
        var refused = await RefusedCreate(_ => Json("<html>bad gateway</html>", HttpStatusCode.BadGateway));

        refused.PushKind.Should().Be(ErpPushFailureKind.OutcomeUnknown);
    }

    [Fact]
    public async Task A_create_answered_without_a_name_leaves_the_outcome_unknown()
    {
        var refused = await RefusedCreate(_ => Json("<html>Login</html>"));

        refused.PushKind.Should().Be(ErpPushFailureKind.OutcomeUnknown,
            "a 200 that is not the ERP's record says nothing about whether the ERP made one");
    }

    [Fact]
    public async Task Too_many_requests_is_transient()
    {
        var refused = await RefusedCreate(_ => Json(string.Empty, HttpStatusCode.TooManyRequests));

        refused.PushKind.Should().Be(ErpPushFailureKind.Transient);
    }

    [Fact]
    public async Task A_timeout_on_a_read_is_transient()
    {
        var (registrar, _) = Answering(_ => throw new TaskCanceledException("timed out"));

        var act = () => registrar.ReadFieldsAsync(ErpRecordType.Supplier, CancellationToken.None);

        (await act.Should().ThrowAsync<ErpRequestException>()).Which.PushKind.Should().Be(ErpPushFailureKind.Transient,
            "a read changes nothing, so it is safe to repeat");
    }

    [Fact]
    public async Task A_timeout_on_a_put_is_transient()
    {
        var (registrar, _) = Answering(_ => throw new TaskCanceledException("timed out"));

        var act = () => registrar.SetContactUserAsync("Zaid-Test Trading Co", "zaid@example.com", CancellationToken.None);

        (await act.Should().ThrowAsync<ErpRequestException>()).Which.PushKind.Should().Be(ErpPushFailureKind.Transient,
            "a PUT replaces what it sends, so sending it again does the same thing");
    }

    [Fact]
    public async Task The_caller_cancelling_is_not_the_erps_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var (registrar, _) = Answering(_ => throw new TaskCanceledException());

        var act = () => registrar.CreateSupplierAsync(Body("{}"), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
