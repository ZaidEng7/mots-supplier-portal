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

        handler.Requests.Skip(1).Should().OnlyContain(r =>
            Uri.UnescapeDataString(r.RequestUri!.Query).Contains("[[\"Dynamic Link\",\"link_doctype\",\"=\",\"Supplier\"]]"),
            "the import filters on the link to a supplier, and a permission can allow the plain list but not that filter");
    }

    [Fact]
    public async Task A_missing_permission_is_named_and_what_works_is_said_too()
    {
        var (probe, _) = ProbeAnswering(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/Address", StringComparison.Ordinal) ? Forbidden() : Ok());

        var result = await probe.TryReachAsync(CancellationToken.None);

        result.Succeeded.Should().BeFalse(
            "the first version read a Company record and called this credential good; the import then failed");
        result.Detail.Should().Contain("addresses (403 PermissionError)");
        result.Detail.Should().Contain("suppliers and contacts can be read");
    }

    [Fact]
    public async Task Every_refusal_is_listed_in_one_answer()
    {
        var (probe, _) = ProbeAnswering(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/Supplier", StringComparison.Ordinal) ? Ok() : Forbidden());

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
    public async Task No_address_configured_says_so()
    {
        var handler = new RoutedHandler(_ => Ok());
        var probe = new ErpSupplierSourceProbe(new HttpClient(handler), new FixedConnection(null));

        (await probe.TryReachAsync(CancellationToken.None)).Detail.Should().Be("No address is configured.");
        handler.Requests.Should().BeEmpty();
    }
}
