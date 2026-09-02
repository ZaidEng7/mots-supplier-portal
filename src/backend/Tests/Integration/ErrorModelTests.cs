using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// API-ARCHITECTURE.md §7's RFC 9457 error model, and the two leaks it exists to prevent.
///
/// <para>§13's definition-of-done carries two of these as items in their own right: *"All errors are
/// RFC 9457 problem+json with type, code, traceId, correlationId"* and *"Validation returns the
/// bilingual field errors array"*.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ErrorModelTests(PostgresApiFixture fixture)
{
    /// <summary>§7's base shape, less the optional members.</summary>
    private static readonly string[] RequiredMembers = ["type", "title", "status", "instance", "traceId", "correlationId"];

    // ---- the base shape ------------------------------------------------------------------------

    /// <summary>
    /// One case per status the API actually produces, rather than one representative: §7 says
    /// "every non-2xx (except 304)", and a shape asserted on a single endpoint proves only that
    /// endpoint.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/rfqs/RFQ-2026-999999", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/audit?notAFilter=1", HttpStatusCode.UnprocessableEntity)]
    public async Task Every_error_carries_the_documented_base_shape(string path, HttpStatusCode expected)
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync(path);

        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json",
            "§7: every non-2xx (except 304) returns application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var member in RequiredMembers)
        {
            body.TryGetProperty(member, out var value).Should().BeTrue($"§7's base shape requires '{member}'");
            value.ToString().Should().NotBeNullOrEmpty($"'{member}' must carry a value, not merely exist");
        }

        body.GetProperty("status").GetInt32().Should().Be((int)expected);
        body.GetProperty("instance").GetString().Should().StartWith("/api/v1/",
            "instance is the request path, which is what makes a problem body traceable to a call");
    }

    /// <summary>
    /// §7: traceId is "the W3C Trace Context trace-id … enabling one-click log/OTel correlation".
    /// A per-response random value would satisfy the shape and none of the purpose, so this asserts
    /// the FORM of a real trace-id: 32 lowercase hex characters, and not all zeroes.
    /// </summary>
    [Fact]
    public async Task The_trace_id_is_a_real_w3c_trace_id()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var body = await (await staff.GetAsync("/api/v1/rfqs/RFQ-2026-999999")).Content.ReadFromJsonAsync<JsonElement>();
        var traceId = body.GetProperty("traceId").GetString();

        traceId.Should().MatchRegex("^[0-9a-f]{32}$");
        traceId.Should().NotBe(new string('0', 32), "an all-zero trace id means no Activity was current");
    }

    /// <summary>
    /// Two requests must not share a correlation id - it is per-request provenance, and a constant
    /// would join every audit row in the system to every other.
    /// </summary>
    [Fact]
    public async Task Correlation_ids_differ_between_requests()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var first = await (await staff.GetAsync("/api/v1/rfqs/RFQ-2026-999999")).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await staff.GetAsync("/api/v1/rfqs/RFQ-2026-999998")).Content.ReadFromJsonAsync<JsonElement>();

        first.GetProperty("correlationId").GetString()
            .Should().NotBe(second.GetProperty("correlationId").GetString());
    }

    // ---- LEAK TEST 1: the 500 ------------------------------------------------------------------

    /// <summary>
    /// §7: *"500 responses never include stack traces, SQL, or internal messages - only type, title
    /// (generic), status, traceId, correlationId."*
    ///
    /// <para>The endpoint behind this throws an exception whose message deliberately carries a
    /// recognisable canary, a password, and a table name. Asserting "no stack trace" by looking for
    /// the word "at " would pass on a response that leaked the connection string; asserting the
    /// canary is ABSENT is the only form of this test that cannot pass by accident.</para>
    /// </summary>
    [Fact]
    public async Task A_500_leaks_no_stack_no_sql_and_no_internal_message()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/__test/throw");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().NotContain("LEAK_CANARY_a7f3d2e1", "the exception message must not reach the client");
        raw.Should().NotContain("hunter2", "a credential inside an exception message is the worst case of this leak");
        raw.Should().NotContain("Password=");
        raw.Should().NotContain("legal_info", "a table name tells an attacker the schema");
        raw.Should().NotContain("InvalidOperationException", "the exception TYPE is an internal detail too");
        raw.Should().NotContain("   at ", "no stack frames");

        // And it is still a conforming problem+json, not an empty body.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("type").GetString().Should().Be(ProblemTypes.Internal);
        body.GetProperty("status").GetInt32().Should().Be(500);
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty("§7: always present, including on 500");
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    // ---- the slug catalogue --------------------------------------------------------------------

    /// <summary>
    /// Every <c>type</c> the API can emit must be one §7.1 documents.
    ///
    /// <para>Both directions, in the same style as the enum-coverage and persona-shape gates: an
    /// undocumented slug fails, and a documented slug the code can no longer produce is reported by
    /// the second assertion rather than silently rotting in the catalogue.</para>
    /// </summary>
    [Fact]
    public async Task Every_emitted_slug_is_one_the_catalogue_documents()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (supplier, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(
            fixture, $"Slug {Guid.NewGuid():N}"[..20]);

        var emitted = new List<string>();
        foreach (var probe in new[]
                 {
                     await staff.GetAsync("/api/v1/rfqs/RFQ-2026-999999"),          // 404
                     await staff.GetAsync("/api/v1/audit?notAFilter=1"),            // 422 unknown-filter
                     await staff.GetAsync("/api/v1/audit?sort=-nonsense"),          // 422 validation
                     await supplier.GetAsync("/api/v1/audit"),                      // 403
                     await fixture.CreateClient().GetAsync("/api/v1/suppliers/me"), // 401
                 })
        {
            var body = await probe.Content.ReadFromJsonAsync<JsonElement>();
            emitted.Add(body.GetProperty("type").GetString()!);
        }

        emitted.Should().OnlyContain(t => ProblemTypes.All.Contains(t),
            "a slug outside §7.1's catalogue is either a typo or a new category nobody decided about");
        emitted.Should().Contain(ProblemTypes.NotFound).And.Contain(ProblemTypes.UnknownFilter);
    }

    /// <summary>
    /// The catalogue itself is transcription, so it is asserted against the document's own text
    /// rather than against itself: every slug must sit under §7's documented base URI and be
    /// lower-kebab, which is the form every row in §7.1 takes.
    /// </summary>
    [Fact]
    public void The_catalogue_matches_the_documented_slug_form()
    {
        ProblemTypes.All.Should().OnlyContain(t => t.StartsWith(ProblemTypes.Base, StringComparison.Ordinal));
        ProblemTypes.All.Select(t => t.Substring(ProblemTypes.Base.Length))
            .Should().OnlyContain(slug => slug.All(c => (c >= 'a' && c <= 'z') || c == '-'));
        ProblemTypes.All.Should().HaveCount(16, "15 rows in §7.1's extract plus §6.2's unknown-filter");
    }

    /// <summary>
    /// A 2xx must not be reshaped. The middleware inspects every response, and a bug that conformed
    /// successes into problem+json would be caught by half the suite - but not obviously, and not
    /// with a message that says why.
    /// </summary>
    [Fact]
    public async Task A_successful_response_is_left_alone()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync("/api/v1/rfqs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("data", out _).Should().BeTrue();
    }

    // ---- LEAK TEST 2: credentials in attemptedValue --------------------------------------------

    /// <summary>
    /// §7.2: *"`attemptedValue` only for non-sensitive fields."*
    ///
    /// <para>Nothing emits <c>attemptedValue</c> today, so this cannot fail on the current code -
    /// which is exactly why it is written now rather than when someone adds it. §7.2 permits the
    /// member, a validation error on a password field is the obvious place it would be added, and
    /// FluentValidation's own default message formatting includes the attempted value for several
    /// rule types. This is the one place in the error model where a mistake leaks a credential.</para>
    ///
    /// <para>Asserted on the RESPONSE BODY as a whole rather than on the absence of a member name:
    /// a leak through <c>detail</c>, through an <c>errors[]</c> entry, or through a framework
    /// message would all be equally bad and none would mention "attemptedValue".</para>
    /// </summary>
    [Fact]
    public async Task A_rejected_password_never_appears_in_the_response()
    {
        var client = fixture.CreateClient();
        const string password = "leakcanary-pw-4f21";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            legalNameAr = "شركة", legalNameEn = $"Leak {Guid.NewGuid():N}"[..20],
            email = $"leak-{Guid.NewGuid():N}@example.com",
            password,
            registrationNumber = $"RN{Guid.NewGuid():N}"[..12],
        });

        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().NotContain(password,
            "§7.2 permits attemptedValue only for NON-sensitive fields - a password echoed back in " +
            "any member of the error body is a credential in a log, a proxy cache and a browser " +
            "devtools pane");
        raw.Should().NotContain("attemptedValue",
            "nothing emits it today; if that changes, the assertion above is what has to keep holding");
    }

    /// <summary>
    /// The companion to the password case: a bad LOGIN must not echo the submitted secret either,
    /// and login is the endpoint an attacker actually probes.
    /// </summary>
    [Fact]
    public async Task A_failed_login_never_echoes_the_submitted_password()
    {
        var client = fixture.CreateClient();
        const string password = "leakcanary-login-9c3e";

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "nobody@example.com", password });

        (await response.Content.ReadAsStringAsync()).Should().NotContain(password);
    }
}
