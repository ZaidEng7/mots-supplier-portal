// Reading suppliers from the ERP, against the bytes the real server actually sent.
//
// THE FIXTURE IS NOT INVENTED. It is the response from the Seven Gates test instance on 2026-09-27, trimmed to
// the fields this client asks for and otherwise untouched - three suppliers with "(seed)" in their own names, no
// tax number, no currency, no linked address or contact, and contact fields that are EMPTY STRINGS rather than
// null. A fixture written by hand would have used null for those, and the one assertion that matters most here
// would have passed without exercising anything.
//
// EMPTY STRING BECOMING NULL IS THE POINT. Whether a supplier can have a portal account turns on whether they
// have an email address, because Supplier.Register requires one and a login needs a mailbox for the password
// link. A caller branching on "is the email null" would treat "" as an address and create an account nobody can
// sign into. So the client normalises here, once, rather than leaving every consumer to remember.
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
        out StubHandler handler)
    {
        handler = new StubHandler(status, body);

        var options = new ErpOptions
        {
            Enabled = true,
            BaseUrl = "http://erp.example:8001",
            ApiKey = "key",
            ApiSecret = "secret",
            Company = "Seven Gates",
        };

        var client = new HttpClient(handler);
        ErpSupplierSource.Configure(client, options);

        return new ErpSupplierSource(client, Options.Create(options), NullLogger<ErpSupplierSource>.Instance);
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
    public async Task Contacts_are_asked_for_when_a_supplier_arrives_without_an_email()
    {
        var source = SourceReturning(HttpStatusCode.OK, LiveSupplierResponse, out var handler);

        await source.ListSuppliersAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(
            2,
            "none of the three suppliers carries an email, and the addresses may be one table over");
        handler.Requests[1].RequestUri!.AbsolutePath.Should().Be("/api/resource/Contact");
        Uri.UnescapeDataString(handler.Requests[1].RequestUri!.Query).Should().Contain(
            "[[\"Dynamic Link\",\"link_doctype\",\"=\",\"Supplier\"]]",
            "listing Dynamic Link is refused to this credential, but filtering Contact on its child rows is not");
    }

    [Fact]
    public async Task Contacts_are_not_asked_for_when_every_supplier_already_has_what_is_needed()
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

        var suppliers = await source.ListSuppliersAsync(CancellationToken.None);

        suppliers[0].Email.Should().Be("a@example.com");
        handler.Requests.Should().HaveCount(
            1,
            "a second call that could change nothing is a call on somebody else's server for no reason");
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

    // Every request is recorded, not just the last one. The client makes a second call for contacts whenever a
    // supplier arrives without an email, and a handler that remembered only the most recent request reported the
    // contact call as though it were the supplier one - which is how the assertion about the supplier URL started
    // failing while the client was behaving correctly.
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests = [];

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        public HttpRequestMessage? Request => _requests.Count == 0 ? null : _requests[0];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
