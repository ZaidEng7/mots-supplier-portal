// Response compression is enabled, proven rather than read off a settings page.
//
// A large enough response, requested with a real encoding header, must come back compressed.
//
// The middleware has a documented threshold below which it does not bother, so the response here is seeded
// comfortably past it.

namespace MotsSupplierPortal.Tests.Integration.Contract;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ResponseCompressionTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_large_JSON_response_is_compressed_when_the_client_accepts_it()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Compression Co {Guid.NewGuid():N}"[..24]);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplierId = await db.Suppliers.OrderByDescending(s => s.CreatedAt).Select(s => s.Id).FirstAsync();

            for (var i = 0; i < 40; i++)
            {
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.CreateVersion7(),
                    OccurredAt = DateTimeOffset.UtcNow.AddSeconds(-i),
                    ActorKind = AuditActorKind.System,
                    AggregateType = "Supplier",
                    AggregateId = supplierId,
                    Action = $"compression_probe_{i:D3}",
                    CorrelationId = Guid.CreateVersion7(),
                });
            }
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/suppliers/me/audit?pageSize=40");
        request.Headers.Add("Accept-Encoding", "gzip, br");

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        response.Content.Headers.ContentEncoding.Should().NotBeEmpty(
            "a large JSON response from a client that accepts gzip/br must come back compressed - " +
            "an absent Content-Encoding header here means the middleware is not actually wired in, " +
            "whatever Program.cs claims");
    }

    [Fact]
    public async Task A_client_that_does_not_accept_compression_gets_an_uncompressed_response()
    {
        var client = fixture.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Accept-Encoding", "identity");

        using var response = await client.SendAsync(request);

        response.Content.Headers.ContentEncoding.Should().BeEmpty(
            "the middleware must not compress when the caller explicitly declines every encoding " +
            "(Accept-Encoding: identity) - proves this is real content negotiation, not an " +
            "unconditional rewrite");
    }
}
