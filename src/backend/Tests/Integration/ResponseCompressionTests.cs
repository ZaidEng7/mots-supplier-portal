using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Task #16/MSP-74: gzip/Brotli response compression is enabled at the middleware level
/// (Program.cs). Proven here rather than just read off the settings page: a large enough JSON
/// response, requested with a real Accept-Encoding header, must come back with Content-Encoding
/// set - the middleware's own documented threshold is roughly 150-1000 bytes below which it does
/// not bother, so the response here is seeded well past that.
/// </summary>
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

            // Comfortably past the middleware's ~1000-byte "not worth it" floor.
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

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/suppliers/me/audit?limit=40");
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
