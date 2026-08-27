using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-65 regression guard: proves the second of two concurrent writers is REJECTED rather than
/// silently overwriting the first (BRULE-098, FR-PROF-010, NFR-AVL-007).
///
/// This is the test that was missing. Optimistic concurrency was mapped, exposed in the DTO, and
/// asserted by three separate requirements — while doing nothing, because no request ever sent the
/// version back. A test at this level (two real HTTP writes against a real Postgres row) is the
/// only thing that can tell "implemented" from "present in the schema".
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class OptimisticConcurrencyTests(PostgresApiFixture fixture)
{
    private static HttpRequestMessage PatchProfile(string description, uint? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/suppliers/me/profile")
        {
            Content = JsonContent.Create(new { description, currencyCode = "SYP" }),
        };

        if (ifMatch is { } version)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        }

        return request;
    }

    [Fact]
    public async Task Second_writer_with_a_stale_row_version_is_rejected_with_a_conflict()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Concurrency Test Co");

        // Both "editors" read the same version — the real-world setup for a lost update.
        var read = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        var sharedVersion = read.GetProperty("rowVersion").GetUInt32();

        // Writer A commits first and moves the row forward.
        var first = await client.SendAsync(PatchProfile("written by A", sharedVersion));
        first.StatusCode.Should().Be(HttpStatusCode.OK, "the first writer holds a current version");

        // Writer B commits second, still holding the now-stale version it read earlier.
        var second = await client.SendAsync(PatchProfile("written by B", sharedVersion));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "BRULE-098: the second writer must be rejected with a conflict, not silently overwritten");

        var conflict = await second.Content.ReadFromJsonAsync<JsonElement>();
        conflict.GetProperty("error").GetString().Should().Be("concurrency_conflict");
        conflict.GetProperty("currentRowVersion").GetUInt32().Should().NotBe(sharedVersion,
            "the client needs the winner's version so it can re-read and retry deliberately");

        // The decisive assertion: A's write survived. Without the guard B overwrites it here and
        // this reads "written by B" — which is exactly the silent data loss being prevented.
        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        after.GetProperty("description").GetString().Should().Be("written by A",
            "the losing writer must not have overwritten the winner's data");
    }

    [Fact]
    public async Task Writer_holding_the_current_row_version_succeeds()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Concurrency Test Co");

        var read = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        var version = read.GetProperty("rowVersion").GetUInt32();

        var response = await client.SendAsync(PatchProfile("fresh version", version));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a caller holding the current version must not be blocked — the guard rejects staleness, not concurrency itself");
    }
}
