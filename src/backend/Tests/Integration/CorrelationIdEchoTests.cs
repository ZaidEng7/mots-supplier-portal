using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Api.Observability;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// EPIC-25's Correlation-Id echo. Audit rows and problem responses already carried a correlation id
/// derived from the trace; the caller's own header was never read, so a client that sent one got a
/// different id back and could not join its log line to the server's.
/// </summary>
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
        // The control for the test above, and the behaviour that must not regress: the trace-derived id
        // was already there, and the echo must not have made it conditional on a request header.
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

        // An id a caller cannot parse back is worse than a generated one: it looks like correlation and
        // joins nothing. The response echoes the id actually in use, so a caller who sent rubbish can see
        // from the reply that theirs was not adopted.
        var malformed = await GetWithAsync(client, "not-a-guid");
        malformed.Headers.GetValues(Header).Single().Should().NotBe("not-a-guid");
        Guid.TryParse(malformed.Headers.GetValues(Header).Single(), out _).Should().BeTrue();

        // All zeroes is what a client sends when its own id was never set. Adopting it would join every
        // such request to every other one.
        var zeroes = Guid.Empty.ToString();
        var empty = await GetWithAsync(client, zeroes);
        empty.Headers.GetValues(Header).Single().Should().NotBe(zeroes);
    }

    [Fact]
    public async Task The_audit_row_carries_the_callers_id_not_a_different_one()
    {
        // The point of the whole feature. The header echo alone would be cosmetic: what a caller needs is
        // for the row the server WROTE to carry the id the caller was using.
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
        // §7's problem document already carried a correlationId; the two must now agree, or a caller
        // reading the body and an operator reading the header would be chasing different requests.
        var client = fixture.CreateRawClient();
        var supplied = Guid.CreateVersion7().ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/suppliers/me");
        request.Headers.TryAddWithoutValidation(Header, supplied);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "no token was presented");
        response.Headers.GetValues(Header).Single().Should().Be(supplied);
    }
}
