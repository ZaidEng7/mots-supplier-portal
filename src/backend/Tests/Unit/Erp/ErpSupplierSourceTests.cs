// Reading suppliers from the ERP, against the bytes the real server actually sent.
//
// THE FIXTURE IS NOT INVENTED. It is the response from the Seven Gates test instance on 2026-09-27, trimmed to
// the fields this client reads and otherwise untouched - three suppliers with "(seed)" in their own names, no
// tax number, no currency, no linked address or contact, and contact fields that are EMPTY STRINGS rather than
// null. A fixture written by hand would have used null for those, and the one assertion that matters most here
// would have passed without exercising anything.
//
// EMPTY STRING BECOMING NULL IS THE POINT. A supplier with no email is given a placeholder login that says so, and
// the import decides that by asking whether the email is null. Asked of "", that question treats it as an address,
// and the supplier gets neither a placeholder nor a login that works. So the client normalises here, once, rather
// than leaving every consumer to remember.
//
// THE REFUSAL TEST USES A REAL REFUSAL TOO - the 403 the test credential gets on Dynamic Link, whose body is the
// ERP's own traceback envelope. It matters that exc_type is read out of that shape rather than from a status
// code alone: 403 means either a missing header or a credential without rights on the record type, and only the
// body distinguishes them.
//
// NOTHING HERE TOUCHES THE NETWORK. The handler is a stub, which is what keeps this suite runnable with no ERP
// and no credential, and the live shape is captured in the fixture rather than fetched.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpSupplierSourceTests
{
    private const string LiveSupplierResponse = """
    {"data": [
      {"name": "Damascus Supplies Co (seed)", "supplier_name": "Damascus Supplies Co (seed)",
       "supplier_group": "Local", "supplier_type": "Company", "tax_id": null, "country": "Syria",
       "email_id": "", "mobile_no": "", "disabled": 0, "default_currency": null,
       "supplier_primary_address": null, "supplier_primary_contact": null,
       "creation": "2026-09-16 12:33:48.421891", "modified": "2026-09-16 12:33:48.421891"},
      {"name": "Homs Linen Mills (seed)", "supplier_name": "Homs Linen Mills (seed)",
       "supplier_group": "Local", "supplier_type": "Company", "tax_id": null, "country": "Syria",
       "email_id": "", "mobile_no": "", "disabled": 0, "default_currency": null,
       "supplier_primary_address": null, "supplier_primary_contact": null,
       "creation": "2026-09-22 11:31:05.044015", "modified": "2026-09-22 11:31:05.044015"},
      {"name": "Tartous Beverages (seed)", "supplier_name": "Tartous Beverages (seed)",
       "supplier_group": "Local", "supplier_type": "Company", "tax_id": null, "country": "Syria",
       "email_id": "", "mobile_no": "", "disabled": 0, "default_currency": null,
       "supplier_primary_address": null, "supplier_primary_contact": null,
       "creation": "2026-09-22 11:31:05.065191", "modified": "2026-09-22 11:31:05.065191"}
    ]}
    """;

    private const string LivePermissionRefusal = """
    {"exception": "frappe.exceptions.PermissionError: Insufficient Permission for Dynamic Link",
     "exc_type": "PermissionError"}
    """;

    private static ErpSupplierSource SourceReturning(
        HttpStatusCode status,
        string body,
        out StubHandler handler,
        string? apiUser = "portal@7gates.example")
    {
        handler = new StubHandler(status, body, apiUser);

        var options = new ErpOptions
        {
            Enabled = true,
            BaseUrl = "http://erp.example:8001",
            ApiKey = "key",
            ApiSecret = "secret",
            Company = "Seven Gates",
        };

        var connection = new ErpConnection(
            "http://erp.example:8001", "key", "secret", IsEnabled: true, ErpConnectionSource.Configuration);

        return new ErpSupplierSource(
            new HttpClient(handler),
            new FixedConnection(connection),
            Options.Create(options),
            NullLogger<ErpSupplierSource>.Instance);
    }

    private sealed class FixedConnection(ErpConnection? connection) : IErpConnectionProvider
    {
        public Task<ErpConnection?> CurrentAsync(CancellationToken ct) => Task.FromResult(connection);
    }

    [Fact]
    public async Task The_suppliers_the_real_server_sent_are_read()
    {
        var source = SourceReturning(HttpStatusCode.OK, LiveSupplierResponse, out _);

        var suppliers = await source.ListSuppliersAsync(CancellationToken.None);

        suppliers.Should().HaveCount(3);
        suppliers[0].ExternalId.Should().Be("Damascus Supplies Co (seed)");
        suppliers[0].Name.Should().Be("Damascus Supplies Co (seed)");
        suppliers[0].SupplierGroup.Should().Be("Local");
        suppliers[0].LegalType.Should().Be("Company");
        suppliers[0].Country.Should().Be("Syria");
        suppliers[0].Disabled.Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_contact_field_is_read_as_absent_rather_than_as_an_address()
    {
        var source = SourceReturning(HttpStatusCode.OK, LiveSupplierResponse, out _);

        var suppliers = await source.ListSuppliersAsync(CancellationToken.None);

        suppliers.Should().OnlyContain(s => s.Email == null,
            "the server sends an empty string, and a consumer branching on null would treat \"\" as a mailbox "
            + "and create an account nobody can sign into");
        suppliers.Should().OnlyContain(s => s.Phone == null);
        suppliers.Should().OnlyContain(s => s.TaxId == null);
        suppliers.Should().OnlyContain(s => s.PrimaryAddressName == null && s.PrimaryContactName == null);
    }

    [Fact]
    public async Task Timestamps_are_read_in_the_erp_servers_zone()
    {
        var source = SourceReturning(HttpStatusCode.OK, LiveSupplierResponse, out _);

        var suppliers = await source.ListSuppliersAsync(CancellationToken.None);

        suppliers[0].CreatedAt.Offset.Should().Be(TimeSpan.FromHours(3));
        suppliers[0].CreatedAt.UtcDateTime.Should().BeCloseTo(
            new DateTime(2026, 9, 16, 9, 33, 48, DateTimeKind.Utc),
            TimeSpan.FromSeconds(1),
            "12:33 in Damascus is 09:33 UTC, and reading it as UTC would be three hours out");
    }

    [Fact]
    public async Task The_request_carries_the_lower_case_token_scheme_and_the_field_list()
    {
        var source = SourceReturning(HttpStatusCode.OK, LiveSupplierResponse, out var handler);

        await source.ListSuppliersAsync(CancellationToken.None);

        handler.Request!.Headers.Authorization!.Scheme.Should().Be("token",
            "the ERP's documentation is explicit that the scheme keyword is case-sensitive, and a capitalised "
            + "one answers 403 with an empty body - which reads exactly like a missing header");
        handler.Request.Headers.Authorization.Parameter.Should().Be("key:secret");
        handler.Request.RequestUri!.ToString().Should().StartWith("http://erp.example:8001/api/resource/Supplier?");
        handler.Request.RequestUri.Query.Should().Contain("fields=");
    }

    [Fact]
    public async Task Contacts_and_addresses_are_read_every_time_filtered_on_their_link_to_suppliers()
    {
        const string complete = """
        {"data": [
          {"name": "A", "supplier_name": "A", "supplier_group": "Local", "supplier_type": "Company",
           "tax_id": "T1", "country": "Syria", "email_id": "a@example.com", "mobile_no": "+963 11 1",
           "disabled": 0, "default_currency": "SYP", "supplier_primary_address": null,
           "supplier_primary_contact": null, "creation": "2026-09-16 12:33:48", "modified": "2026-09-16 12:33:48"}
        ]}
        """;

        var source = SourceReturning(HttpStatusCode.OK, complete, out var handler);

        await source.ListSuppliersAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(
            4,
            "the contact carries the person's name and the address is its own record, so both are read even for a "
            + "supplier that already has an email and a phone, and the portal's own API user is asked once");
        handler.Requests[1].RequestUri!.AbsolutePath.Should().Be("/api/resource/Contact");
        Uri.UnescapeDataString(handler.Requests[1].RequestUri!.Query).Should().Contain(
            "[[\"Dynamic Link\",\"link_doctype\",\"=\",\"Supplier\"]]",
            "listing Dynamic Link is refused to this credential, but filtering Contact on its child rows is not");
        handler.Requests[2].RequestUri!.AbsolutePath.Should().Be("/api/resource/Address");
        Uri.UnescapeDataString(handler.Requests[2].RequestUri!.Query).Should().Contain(
            "[[\"Dynamic Link\",\"link_doctype\",\"=\",\"Supplier\"]]");
        handler.Requests[3].RequestUri!.AbsolutePath.Should().Be("/api/method/frappe.auth.get_logged_user");
    }

    // THE PORTAL'S OWN CREATES ARE MARKED BY THEIR OWNER. A supplier the push created and has not linked yet would
    // otherwise reach the import as a stranger, and the import would make a second portal supplier of it. The owner
    // is compared with the user the credential signs in as, asked once for the whole list, and in any case.
    [Fact]
    public async Task A_supplier_the_portals_own_api_user_created_is_marked_as_the_portals()
    {
        const string owned = """
        {"data": [
          {"name": "SUP-2026-00092", "supplier_name": "Pushed Trading", "owner": "Portal@7Gates.example",
           "disabled": 0, "creation": "2026-09-30 12:00:00", "modified": "2026-09-30 12:00:00"},
          {"name": "SUP-2026-00001", "supplier_name": "Entered by hand", "owner": "accountant@7gates.example",
           "disabled": 0, "creation": "2026-08-10 12:20:11", "modified": "2026-09-17 10:38:20"},
          {"name": "SUP-2026-00002", "supplier_name": "No owner sent", "disabled": 0,
           "creation": "2026-08-10 12:20:11", "modified": "2026-09-17 10:38:20"}
        ]}
        """;

        var source = SourceReturning(HttpStatusCode.OK, owned, out var handler, apiUser: "portal@7gates.example");

        var suppliers = await source.ListSuppliersAsync(CancellationToken.None);

        suppliers.Single(s => s.ExternalId == "SUP-2026-00092").CreatedByPortal.Should().BeTrue();
        suppliers.Single(s => s.ExternalId == "SUP-2026-00001").CreatedByPortal.Should().BeFalse();
        suppliers.Single(s => s.ExternalId == "SUP-2026-00002").CreatedByPortal.Should().BeFalse();
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath.StartsWith("/api/method/", StringComparison.Ordinal))
            .Should().Be(1, "the API user is asked once for the list, not once per supplier");
    }

    [Fact]
    public async Task An_api_user_read_that_is_not_the_erps_answer_fails_the_read_rather_than_marking_nothing()
    {
        var source = SourceReturning(HttpStatusCode.OK, LiveSupplierResponse, out _, apiUser: null);

        var act = () => source.ListSuppliersAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<ErpRequestException>()).Which.Message.Should().Contain(
            "API user",
            "not knowing the API user would let the import take the push's own creates for strangers");
    }

    [Fact]
    public async Task Every_supplier_field_is_asked_for_so_a_server_without_the_custom_ones_is_still_readable()
    {
        var source = SourceReturning(HttpStatusCode.OK, LiveSupplierResponse, out var handler);

        await source.ListSuppliersAsync(CancellationToken.None);

        Uri.UnescapeDataString(handler.Request!.RequestUri!.Query).Should().Contain(
            "fields=[\"*\"]",
            "naming a custom field a server does not have is an error there, and the test instance has none of them");
    }

    [Fact]
    public async Task The_fields_seven_gates_added_are_read_when_the_server_has_them()
    {
        const string real = """
        {"data": [
          {"name": "SUP-2026-00001", "supplier_name": "AL-Zaeim for advertising services",
           "supplier_group": "مستلزمات مكتبية - SYP", "supplier_type": "Company", "tax_id": "01010484153",
           "country": "Jordan", "email_id": "", "mobile_no": "", "disabled": 0, "default_currency": "SYP",
           "supplier_primary_address": null, "supplier_primary_contact": null,
           "custom_supplier_arabic_name": "الزعيم للخدمات الإعلانية", "custom_registration_number": "73260",
           "custom_registration_type": "Commercial", "workflow_state": "Approved", "supplier_details": null,
           "creation": "2026-08-10 12:20:11.298784", "modified": "2026-09-17 10:38:20.362759"}
        ]}
        """;

        var source = SourceReturning(HttpStatusCode.OK, real, out _);

        var supplier = (await source.ListSuppliersAsync(CancellationToken.None)).Single();

        supplier.ArabicName.Should().Be("الزعيم للخدمات الإعلانية");
        supplier.RegistrationNumber.Should().Be("73260");
        supplier.RegistrationType.Should().Be("Commercial");
        supplier.WorkflowState.Should().Be("Approved");
        supplier.Description.Should().BeNull();
    }

    [Fact]
    public async Task A_refusal_carries_its_status_its_exc_type_and_what_to_do_about_it()
    {
        var source = SourceReturning(HttpStatusCode.Forbidden, LivePermissionRefusal, out _);

        var act = () => source.ListSuppliersAsync(CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ErpRequestException>();
        thrown.Which.Status.Should().Be(HttpStatusCode.Forbidden);
        thrown.Which.ExcType.Should().Be("PermissionError",
            "403 means either a missing header or a credential without rights, and only the body tells them "
            + "apart");
        thrown.Which.Kind.Should().Be(ErpFailureKind.CredentialOrPermission);
    }

    [Fact]
    public async Task A_refusal_with_no_readable_body_still_names_its_status()
    {
        var source = SourceReturning(HttpStatusCode.ServiceUnavailable, "<html>gateway</html>", out _);

        var act = () => source.ListSuppliersAsync(CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ErpRequestException>();
        thrown.Which.ExcType.Should().BeNull("a proxy's HTML is not the ERP's error envelope");
        thrown.Which.Kind.Should().Be(ErpFailureKind.Transient);
    }

    // THE SUPPLIER GROUPS, for the integrations screen. Unlike the supplier fixture above, this answer is written by
    // hand: nothing has asked either server for its groups yet. It has the ERP's list shape, and the Arabic group is
    // one the real server holds on a supplier. The blank row and the repeat stand for what a hand-kept tree can hold,
    // and neither is a group anybody can choose.
    [Fact]
    public async Task The_supplier_groups_are_read_trimmed_once_each_and_without_the_headings()
    {
        const string groups = """
        {"data": [
          {"name": "Local Suppliers - SYP"},
          {"name": " مستلزمات مكتبية - SYP "},
          {"name": ""},
          {"name": "Local Suppliers - SYP"}
        ]}
        """;

        var source = SourceReturning(HttpStatusCode.OK, groups, out var handler);

        var read = await source.ListSupplierGroupsAsync(CancellationToken.None);

        read.Should().Equal("Local Suppliers - SYP", "مستلزمات مكتبية - SYP");
        handler.Requests.Should().ContainSingle("the groups are one read, and the import's three reads are not made");
        handler.Request!.Headers.Authorization!.Scheme.Should().Be("token");
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/resource/Supplier%20Group");
        Uri.UnescapeDataString(handler.Request.RequestUri.Query).Should().Contain(
            "filters=[[\"Supplier Group\",\"is_group\",\"=\",0]]",
            "a group that holds other groups is a heading in the ERP's tree, not a place to file a supplier");
    }

    [Fact]
    public async Task A_refused_group_read_says_it_was_the_groups_that_were_refused()
    {
        var source = SourceReturning(HttpStatusCode.Forbidden, LivePermissionRefusal, out _);

        var act = () => source.ListSupplierGroupsAsync(CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ErpRequestException>();
        thrown.Which.Message.Should().Contain("supplier group read",
            "an administrator told the ERP refused a supplier read would go looking at the import's rights");
        thrown.Which.ExcType.Should().Be("PermissionError");
    }

    // Every request is recorded, not just the last one. The client makes a call for contacts and another for addresses
    // after the supplier list, and a handler that remembered only the most recent request reported one of those as
    // though it were the supplier call - which is how the assertion about the supplier URL once started failing while
    // the client was behaving correctly. The API user read is answered as the ERP answers it, with the user in
    // "message"; a null user answers it with the list body, which is not the ERP's answer to that read.
    private sealed class StubHandler(HttpStatusCode status, string body, string? apiUser) : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests = [];

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        public HttpRequestMessage? Request => _requests.Count == 0 ? null : _requests[0];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);

            var answer = apiUser is not null && request.RequestUri!.AbsolutePath == "/api/method/frappe.auth.get_logged_user"
                ? $$"""{"message": "{{apiUser}}"}"""
                : body;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/json"),
            });
        }
    }
}
