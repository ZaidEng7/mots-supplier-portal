using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// A reshaped error response must survive compression.
///
/// <para><b>The defect this pins.</b> `UseResponseCompression` sat UNDER
/// `ProblemDetailsMiddleware`, which swaps the response body stream to rewrite a handler's
/// <c>{ error: "..." }</c> into §7's problem+json. The rewritten bytes were written past a layer that
/// had already declared <c>Content-Encoding: gzip</c>, so a browser got a gzip header and a body that
/// was not gzip — reported as ERR_CONTENT_DECODING_FAILED and handed to the SPA as an EMPTY body.
/// Every browser sends Accept-Encoding, so in practice no reshaped error was readable by the SPA.</para>
///
/// <para>Success responses were never affected — they are not reshaped — which is why this survived
/// for so long, and is why the control below asserts a 200 still compresses. A "fix" that simply
/// stopped compressing would pass the first test and fail the second.</para>
///
/// <para><b>Which of these actually discriminates, measured rather than assumed.</b> Reverting the
/// middleware order turns ONE of the three red: <c>The_same_error_is_identical_with_and_without_compression</c>.
/// The other two pass either way, because the in-process TestServer does not reproduce what a browser
/// does with a mislabelled encoding — <c>HttpClient</c> is more forgiving than Chrome. They are kept
/// as controls, not as proof. The defect was found in a browser and confirmed with
/// <c>curl --compressed</c>; this file pins the half of it a test host can see.</para>
/// </summary>
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
        // reset-password, because it predates the change that found this: the handler answers
        // { error: "invalid_or_expired_token" } and the middleware reshapes it.
        var response = await Compressing().PostAsJsonAsync("/api/v1/auth/reset-password",
            new { token = "not-a-real-token", newPassword = "SomeLongPassword#1" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The body, not just the status. Before the fix this read as empty and the assertion below
        // threw on parsing rather than on content - which is exactly what the SPA experienced.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeEmpty("a compressed error response must still carry its body");

        using var problem = JsonDocument.Parse(body);
        problem.RootElement.TryGetProperty("code", out _).Should().BeTrue("§7's machine-stable code is what a client branches on");
    }

    [Fact]
    public async Task A_successful_response_is_still_compressed()
    {
        // The control. Compression must not have been turned off to make the test above pass.
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        var response = await client.GetAsync("/api/v1/reference/categories");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Contain("gzip");
    }

    [Fact]
    public async Task The_same_error_is_identical_with_and_without_compression()
    {
        // Both directions of the guard: the fix must not have changed WHAT the error says, only
        // whether it arrives. A middleware reordering that silently altered a payload would pass
        // both tests above.
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
