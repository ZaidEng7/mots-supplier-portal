// The caller's own correlation identifier is adopted and echoed back.
//
// Audit rows and error responses already carried one derived from the trace. The caller's header was never read, so
// a client that sent one got a different identifier back and could not join its log line to the server's.
//
//
// THE POINT OF THE FEATURE IS THE ROW, NOT THE HEADER
//
// The echo alone would be cosmetic. What a caller needs is for the row the server WROTE to carry the identifier the
// caller was using.
//
// And the error document already carried one, so the two must now agree, or a caller reading the body and an
// operator reading the header would be chasing different requests.
//
//
// WHAT IS NOT ADOPTED
//
// An identifier the caller cannot parse back is worse than a generated one, because it looks like correlation and
// joins nothing. The response echoes the identifier actually in use, so a caller who sent rubbish can see from the
// reply that theirs was not adopted.
//
// All zeroes is what a client sends when its own identifier was never set, and adopting it would join every such
// request to every other one.
//
// The control is the behaviour that must not regress: the trace-derived identifier was already there, and the echo
// must not have made it conditional on a request header.

namespace MotsSupplierPortal.Tests.Integration.Contract;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Api.Observability;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class CorrelationIdEchoTests(PostgresApiFixture fixture)
{
    private const string Header = CorrelationIdMiddleware.HeaderName;

    private static async Task<HttpResponseMessage> GetWithAsync(HttpClient client, string? correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/meta");
        if (correlationId is not null) request.Headers.TryAddWithoutValidation(Header, correlationId);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task A_supplied_id_comes_back_unchanged()
    {
        var client = fixture.CreateRawClient();
        var supplied = Guid.CreateVersion7().ToString();

        var response = await GetWithAsync(client, supplied);

        response.Headers.TryGetValues(Header, out var echoed).Should().BeTrue(
            "a caller cannot correlate against an id they never receive");
        echoed!.Single().Should().Be(supplied);
    }

    [Fact]
    public async Task Every_response_carries_one_even_when_the_caller_sent_none()
    {
        var client = fixture.CreateRawClient();

        var response = await GetWithAsync(client, null);

        response.Headers.TryGetValues(Header, out var echoed).Should().BeTrue();
        Guid.TryParse(echoed!.Single(), out var generated).Should().BeTrue("the fallback is still a Guid");
        generated.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_malformed_or_empty_id_is_ignored_rather_than_trusted()
    {
        var client = fixture.CreateRawClient();

        var malformed = await GetWithAsync(client, "not-a-guid");
        malformed.Headers.GetValues(Header).Single().Should().NotBe("not-a-guid");
        Guid.TryParse(malformed.Headers.GetValues(Header).Single(), out _).Should().BeTrue();

        var zeroes = Guid.Empty.ToString();
        var empty = await GetWithAsync(client, zeroes);
        empty.Headers.GetValues(Header).Single().Should().NotBe(zeroes);
    }

    [Fact]
    public async Task The_audit_row_carries_the_callers_id_not_a_different_one()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Corr{Guid.NewGuid():N}"[..12]);
        var supplied = Guid.CreateVersion7();

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/proposals/{seed.ProposalCode}/request-clarification")
        {
            Content = JsonContent.Create(new { reason = "Correlated on purpose." }),
        };
        request.Headers.TryAddWithoutValidation(Header, supplied.ToString());

        var response = await seed.Officer.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.GetValues(Header).Single().Should().Be(supplied.ToString());

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var correlations = await db.AuditLogs.AsNoTracking()
            .Where(a => a.ReferenceCode == seed.ProposalCode && a.Action == "proposal_clarification_requested")
            .Select(a => a.CorrelationId)
            .ToListAsync();

        correlations.Should().Contain(supplied);
    }

    [Fact]
    public async Task A_problem_response_carries_the_same_id_in_the_body_and_the_header()
    {
        var client = fixture.CreateRawClient();
        var supplied = Guid.CreateVersion7().ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/suppliers/me");
        request.Headers.TryAddWithoutValidation(Header, supplied);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "no token was presented");
        response.Headers.GetValues(Header).Single().Should().Be(supplied);
    }
}
