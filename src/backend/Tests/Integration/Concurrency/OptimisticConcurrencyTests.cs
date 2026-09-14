// MSP-65's regression guard: it proves the second of two concurrent writers is REJECTED rather than
// silently overwriting the first, per BRULE-098, FR-PROF-010 and NFR-AVL-007.
//
// This is the test that was missing. Optimistic concurrency was mapped, exposed in the DTO and asserted by
// three separate requirements - while doing nothing, because no request ever sent the version back. A test
// at this level, two real HTTP writes against a real Postgres row, is the only thing that can tell
// "implemented" from "present in the schema".
//
// Rewritten for §8.1 under T3-34. The contract MSP-65 invented - a bare decimal If-Match and a 409 carrying
// currentRowVersion - is superseded: the version travels as a base64url ETag, and a stale write is 412
// ETAG_MISMATCH, because a lost update is a failed precondition rather than one of §7.1's three conflicts.
// The behaviour being proven is unchanged; only its wire form is. The encoding helper is §8.1's format
// produced by the same code the server reads it with, because a test that hand-rolled the encoding could
// pass against a server that encodes differently, and the version is taken from the ETag header the server
// actually sent rather than re-encoded from the body, which would leave the header itself unproven.
//
// Both editors read the same version, which is the real-world setup for a lost update. Writer A commits
// first and moves the row forward; writer B commits second still holding the version it read earlier. The
// decisive assertion is that A's write survived: without the guard B overwrites it here and the row reads
// "written by B", which is exactly the silent data loss being prevented.
//
// The profile PATCH is addressed by supplier code now, per §12-A/C3 and §12.2.

namespace MotsSupplierPortal.Tests.Integration.Concurrency;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class OptimisticConcurrencyTests(PostgresApiFixture fixture)
{
    private static HttpRequestMessage PatchProfile(string supplierCode, string description, uint? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/suppliers/{supplierCode}")
        {
            Content = JsonContent.Create(new { description, currencyCode = "SYP" }),
        };

        if (ifMatch is { } version)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ETag.ForPrecondition(version));
        }

        return request;
    }

    private static uint VersionFrom(HttpResponseMessage response)
    {
        response.Headers.ETag.Should().NotBeNull("§8.1: every read of a mutable aggregate returns its version as an ETag");
        ETag.TryParse(response.Headers.ETag!.ToString(), out var version).Should().BeTrue();
        return version;
    }

    [Fact]
    public async Task Second_writer_with_a_stale_row_version_is_rejected_with_a_conflict()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Concurrency Test Co");

        var readResponse = await client.GetAsync("/api/v1/suppliers/me");
        var read = await readResponse.Content.ReadFromJsonAsync<JsonElement>();
        var supplierCode = read.GetProperty("supplierCode").GetString()!;
        var sharedVersion = VersionFrom(readResponse);

        var first = await client.SendAsync(PatchProfile(supplierCode, "written by A", sharedVersion));
        first.StatusCode.Should().Be(HttpStatusCode.OK, "the first writer holds a current version");

        var second = await client.SendAsync(PatchProfile(supplierCode, "written by B", sharedVersion));

        second.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed,
            "§8.1: a stale If-Match is 412 ETAG_MISMATCH - BRULE-098's rejection, in the documented status");

        var conflict = await second.Content.ReadFromJsonAsync<JsonElement>();
        conflict.GetProperty("code").GetString().Should().Be("ETAG_MISMATCH");
        VersionFrom(second).Should().NotBe(sharedVersion,
            "the client needs the winner's version so it can re-read and retry deliberately");

        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        after.GetProperty("description").GetString().Should().Be("written by A",
            "the losing writer must not have overwritten the winner's data");
    }

    [Fact]
    public async Task Writer_holding_the_current_row_version_succeeds()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Concurrency Test Co");

        var readResponse = await client.GetAsync("/api/v1/suppliers/me");
        var read = await readResponse.Content.ReadFromJsonAsync<JsonElement>();
        var supplierCode = read.GetProperty("supplierCode").GetString()!;
        var version = VersionFrom(readResponse);

        var response = await client.SendAsync(PatchProfile(supplierCode, "fresh version", version));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a caller holding the current version must not be blocked — the guard rejects staleness, not concurrency itself");
    }
}
