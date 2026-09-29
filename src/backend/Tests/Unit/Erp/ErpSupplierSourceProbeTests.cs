// What the connection test says, for each way the ERP can answer.
//
// THE TEST MUST BE ABLE TO SAY NO WHEN THE IMPORT WOULD FAIL. The first version read one Company record, so a
// credential with no right to read suppliers passed it and the import then failed. The partial-permission case is
// therefore the one that matters: suppliers and contacts readable, addresses refused - and the answer must name
// exactly what is missing and what is fine, because that sentence is what an administrator sends the ERP team.
//
// THE CONTROL IS ALL THREE READABLE, which must be a success. A probe that failed everything would pass the refusal
// tests.
//
// ONLY A 403 IS CALLED A MISSING PERMISSION. A rejected key, a server that is down and a page that is not the ERP's
// API each get their own answer, and each test here also checks that the permission sentence is absent - that
// sentence would send an administrator to ask for rights the account already has.
//
// A MALFORMED ADDRESS IS AN ANSWER, NOT AN EXCEPTION. An exception there left the previous test's result on screen.
//
// An address that cannot be reached at all is reported once and the remaining reads are not attempted: the second and
// third would fail the same way and add three identical sentences.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using System.Net;
using System.Text;
using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpSupplierSourceProbeTests
{
    private const string Refusal = """{"exc_type": "PermissionError", "exception": "frappe.exceptions.PermissionError"}""";

    private sealed class RoutedHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private sealed class FixedConnection(ErpConnection? connection) : IErpConnectionProvider
    {
        public Task<ErpConnection?> CurrentAsync(CancellationToken ct) => Task.FromResult(connection);
    }

    private static readonly ErpConnection Connection =
        new("http://erp.example", "key", "secret", IsEnabled: true, ErpConnectionSource.Database);

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent("""{"data": []}""") };

    private static HttpResponseMessage Forbidden() =>
        new(HttpStatusCode.Forbidden) { Content = new StringContent(Refusal, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Answer(HttpStatusCode status, string body, string mediaType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static bool IsFor(HttpRequestMessage request, string doctype) =>
        request.RequestUri!.AbsolutePath.EndsWith("/" + doctype, StringComparison.Ordinal);

    private static (ErpSupplierSourceProbe Probe, RoutedHandler Handler) ProbeAnswering(
        Func<HttpRequestMessage, HttpResponseMessage> answer, ErpConnection? connection = null)
    {
        var handler = new RoutedHandler(answer);
        return (new ErpSupplierSourceProbe(new HttpClient(handler), new FixedConnection(connection ?? Connection)), handler);
    }

    [Fact]
    public async Task All_three_readable_is_a_success_that_says_the_import_can_run()
    {
        var (probe, handler) = ProbeAnswering(_ => Ok());

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Detail.Should().Contain("suppliers, contacts and addresses");
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().Equal(
            "/api/resource/Supplier", "/api/resource/Contact", "/api/resource/Address");
        handler.Requests.Should().OnlyContain(r => r.Headers.Authorization!.ToString() == "token key:secret");
    }

    [Fact]
    public async Task Contacts_and_addresses_are_read_the_way_the_import_reads_them()
    {
        var (probe, handler) = ProbeAnswering(_ => Ok());

        await probe.TryReachAsync(CancellationToken.None);

        var queries = handler.Requests.Select(r => Uri.UnescapeDataString(r.RequestUri!.Query)).ToList();

        queries.Skip(1).Should().OnlyContain(q => q.Contains("[[\"Dynamic Link\",\"link_doctype\",\"=\",\"Supplier\"]]"),
            "the import filters on the link to a supplier, and a permission can allow the plain list but not that filter");
        queries[0].Should().Contain("fields=[\"*\"]");
        queries[1].Should().Contain("\"email_id\"", "a field the credential may not read has to show up in the test");
        queries[2].Should().Contain("\"address_line1\"");
        queries.Should().OnlyContain(q => q.Contains("limit_page_length=1"));
    }

    [Fact]
    public async Task A_missing_permission_is_named_and_what_works_is_said_too()
    {
        var (probe, _) = ProbeAnswering(r => IsFor(r, "Address") ? Forbidden() : Ok());

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Succeeded.Should().BeFalse(
            "the first version read a Company record and called this credential good; the import then failed");
        result.Detail.Should().Contain("addresses (403 PermissionError)");
        result.Detail.Should().Contain("suppliers and contacts can be read");
        result.Detail.Should().Contain("needs read access on all three");
    }

    [Fact]
    public async Task Every_refusal_is_listed_in_one_answer()
    {
        var (probe, _) = ProbeAnswering(r => IsFor(r, "Supplier") ? Ok() : Forbidden());

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Detail.Should().Contain("contacts (403 PermissionError)").And.Contain("addresses (403 PermissionError)");
    }

    [Fact]
    public async Task An_address_that_cannot_be_reached_is_reported_once()
    {
        var (probe, handler) = ProbeAnswering(_ => throw new HttpRequestException("Connection refused"));

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Detail.Should().Contain("Could not reach http://erp.example").And.Contain("Connection refused");
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_rejected_key_says_so_and_does_not_ask_for_permissions()
    {
        var (probe, _) = ProbeAnswering(_ => Answer(
            HttpStatusCode.Unauthorized, """{"exc_type": "AuthenticationError"}""", "application/json"));

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Detail.Should().Contain("did not accept the key and secret (401 AuthenticationError)");
        result.Detail.Should().NotContain("read access");
    }

    [Fact]
    public async Task A_server_that_is_down_is_not_called_a_missing_permission()
    {
        var (probe, handler) = ProbeAnswering(_ => Answer(HttpStatusCode.BadGateway, "<html>502</html>", "text/html"));

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Detail.Should().Contain("suppliers (502 BadGateway)").And.Contain("did not come back as the ERP's data");
        result.Detail.Should().NotContain("read access");
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_page_that_answers_200_but_is_not_the_erps_list_is_not_a_pass()
    {
        var (probe, _) = ProbeAnswering(_ => Answer(HttpStatusCode.OK, "<!DOCTYPE html><html>Login</html>", "text/html"));

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Succeeded.Should().BeFalse("the import would fail on the first character of an HTML page");
        result.Detail.Should().Contain("suppliers (200 OK text/html)");
    }

    [Fact]
    public async Task A_refusal_whose_body_is_not_a_json_object_is_still_a_refusal()
    {
        var (probe, handler) = ProbeAnswering(r =>
            IsFor(r, "Address") ? Answer(HttpStatusCode.Forbidden, "\"Forbidden\"", "application/json") : Ok());

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Detail.Should().Contain("addresses (403 Forbidden)").And.Contain("suppliers and contacts can be read");
        handler.Requests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("9.160.106.141:8001")]
    [InlineData("erp.example.com")]
    [InlineData("http://erp example.com")]
    public async Task An_address_that_is_not_a_full_url_is_an_answer_rather_than_an_exception(string address)
    {
        var (probe, handler) = ProbeAnswering(
            _ => Ok(), new ErpConnection(address, "key", "secret", IsEnabled: true, ErpConnectionSource.Database));

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Detail.Should().Contain("needs to start with http:// or https://");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_read_that_fails_after_the_erp_answered_keeps_what_was_learned()
    {
        var (probe, _) = ProbeAnswering(r =>
            IsFor(r, "Supplier") ? Ok() : throw new HttpRequestException("Connection reset by peer"));

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Detail.Should().StartWith("Reached http://erp.example").And.Contain("contacts (no answer: Connection reset by peer)");
    }

    [Fact]
    public async Task An_untrusted_certificate_shows_why_rather_than_see_inner_exception()
    {
        var (probe, _) = ProbeAnswering(_ => throw new HttpRequestException(
            "The SSL connection could not be established, see inner exception.",
            new System.Security.Authentication.AuthenticationException("The remote certificate is invalid: UntrustedRoot")));

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Detail.Should().Contain("UntrustedRoot");
    }

    [Fact]
    public async Task A_pass_on_a_switched_off_connection_says_the_import_will_not_run_yet()
    {
        var (probe, _) = ProbeAnswering(
            _ => Ok(), new ErpConnection("http://erp.example", "key", "secret", IsEnabled: false, ErpConnectionSource.Database));

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Detail.Should().Contain("switched off");
    }

    [Fact]
    public async Task No_address_configured_says_so()
    {
        var handler = new RoutedHandler(_ => Ok());
        var probe = new ErpSupplierSourceProbe(new HttpClient(handler), new FixedConnection(null));

        (await probe.TryReachAsync(CancellationToken.None)).Detail.Should().Be("No address is configured.");
        handler.Requests.Should().BeEmpty();
    }
}
