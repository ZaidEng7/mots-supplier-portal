// A reshaped error response must survive compression.
//
//
// THE DEFECT THIS PINS
//
// Compression sat UNDER the error middleware, which swaps the response body stream to rewrite a handler's ad-hoc
// error into the standard shape.
//
// The rewritten bytes were written past a layer that had already declared the body compressed, so a browser got a
// compression header and a body that was not compressed. It reported a decoding failure and handed the interface an
// EMPTY body.
//
// Every browser asks for compression, so in practice no reshaped error was readable by the interface at all.
//
// Success responses were never affected, because they are not reshaped, which is why this survived for so long, and
// it is why the control asserts a success still compresses: a "fix" that simply stopped compressing would pass the
// first test and fail the second.
//
//
// WHICH OF THESE ACTUALLY DISCRIMINATES, MEASURED RATHER THAN ASSUMED
//
// Reverting the middleware order turns ONE of the three red: the one comparing the same error with and without
// compression.
//
// The other two pass either way, because the in-process test host does not reproduce what a browser does with a
// mislabelled encoding: its client is more forgiving.
//
// They are kept as controls rather than as proof. The defect was found in a browser and confirmed on the command
// line; this file pins the half of it a test host can see.

namespace MotsSupplierPortal.Tests.Integration.Contract;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class CompressedErrorBodyTests(PostgresApiFixture fixture)
{
    private HttpClient Compressing()
    {
        var client = fixture.CreateRawClient();
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        return client;
    }

    [Fact]
    public async Task A_reshaped_error_is_readable_when_the_caller_accepts_gzip()
    {
        var response = await Compressing().PostAsJsonAsync("/api/v1/auth/reset-password",
            new { token = "not-a-real-token", newPassword = "SomeLongPassword#1" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeEmpty("a compressed error response must still carry its body");

        using var problem = JsonDocument.Parse(body);
        problem.RootElement.TryGetProperty("code", out _).Should().BeTrue("§7's machine-stable code is what a client branches on");
    }

    [Fact]
    public async Task A_successful_response_is_still_compressed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        var response = await client.GetAsync("/api/v1/reference/categories");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Contain("gzip");
    }

    [Fact]
    public async Task The_same_error_is_identical_with_and_without_compression()
    {
        var compressed = await Compressing().PostAsJsonAsync("/api/v1/auth/reset-password",
            new { token = "not-a-real-token", newPassword = "SomeLongPassword#1" });
        var plain = await fixture.CreateRawClient().PostAsJsonAsync("/api/v1/auth/reset-password",
            new { token = "not-a-real-token", newPassword = "SomeLongPassword#1" });

        var a = JsonDocument.Parse(await compressed.Content.ReadAsStringAsync()).RootElement;
        var b = JsonDocument.Parse(await plain.Content.ReadAsStringAsync()).RootElement;

        a.GetProperty("code").GetString().Should().Be(b.GetProperty("code").GetString());
        a.GetProperty("status").GetInt32().Should().Be(b.GetProperty("status").GetInt32());
    }
}
