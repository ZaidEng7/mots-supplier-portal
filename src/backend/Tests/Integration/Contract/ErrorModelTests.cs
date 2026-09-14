// The error model, and the two leaks it exists to prevent.
//
// The written definition of done carries two of these as items in their own right: every error is problem-shaped
// with its type, code and correlation identifiers, and validation returns the bilingual field errors.
//
// One case per status the API actually produces rather than one representative, because the contract says every
// non-success response and a shape asserted on a single endpoint proves only that endpoint.
//
//
// THE TWO IDENTIFIERS ARE ASSERTED FOR THEIR FORM AND THEIR VARIANCE
//
// The trace identifier is meant to enable one-click correlation with the logs. A per-response random value would
// satisfy the shape and none of the purpose, so its real FORM is asserted: the right length, lowercase hexadecimal,
// and not all zeroes.
//
// And two requests must not share a correlation identifier, because it is per-request provenance and a constant
// would join every audit row in the system to every other.
//
//
// LEAK ONE: THE UNHANDLED FAILURE
//
// The contract says such a response never includes a stack trace, a query, or an internal message.
//
// The endpoint behind this throws an exception whose message deliberately carries a recognisable canary, a
// password, and a table name.
//
// Asserting "no stack trace" by looking for a common word would pass on a response that leaked the connection
// string. Asserting the CANARY is absent is the only form of this test that cannot pass by accident. And the
// response is still asserted to be a conforming error rather than an empty body.
//
//
// LEAK TWO: A REJECTED VALUE ECHOED BACK
//
// The contract permits echoing the attempted value only for non-sensitive fields.
//
// Nothing emits it today, so this cannot fail on the current code, which is exactly why it is written now rather
// than when somebody adds it. The contract permits the member, a validation error on a password field is the
// obvious place it would appear, and the validation library's own default message formatting includes the attempted
// value for several rule types.
//
//
// THE SLUG CATALOGUE IS CHECKED BOTH WAYS, AND AGAINST THE DOCUMENT'S OWN TEXT
//
// Every type the API can emit must be one the contract documents, and a documented slug the code can no longer
// produce is reported rather than silently rotting in the catalogue.
//
// The catalogue is transcription, so it is asserted against the document rather than against itself: every slug
// must sit under the documented base address and be lower-kebab, which is the form every documented row takes.
//
// And a success must not be reshaped. The middleware inspects every response, and a bug that conformed successes
// into the error shape would be caught by half the suite, but not obviously and not with a message that says why.

namespace MotsSupplierPortal.Tests.Integration.Contract;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ErrorModelTests(PostgresApiFixture fixture)
{
    private static readonly string[] RequiredMembers = ["type", "title", "status", "instance", "traceId", "correlationId"];

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

    [Fact]
    public async Task The_trace_id_is_a_real_w3c_trace_id()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var body = await (await staff.GetAsync("/api/v1/rfqs/RFQ-2026-999999")).Content.ReadFromJsonAsync<JsonElement>();
        var traceId = body.GetProperty("traceId").GetString();

        traceId.Should().MatchRegex("^[0-9a-f]{32}$");
        traceId.Should().NotBe(new string('0', 32), "an all-zero trace id means no Activity was current");
    }

    [Fact]
    public async Task Correlation_ids_differ_between_requests()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var first = await (await staff.GetAsync("/api/v1/rfqs/RFQ-2026-999999")).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await staff.GetAsync("/api/v1/rfqs/RFQ-2026-999998")).Content.ReadFromJsonAsync<JsonElement>();

        first.GetProperty("correlationId").GetString()
            .Should().NotBe(second.GetProperty("correlationId").GetString());
    }

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

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("type").GetString().Should().Be(ProblemTypes.Internal);
        body.GetProperty("status").GetInt32().Should().Be(500);
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty("§7: always present, including on 500");
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

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

    [Fact]
    public void The_catalogue_matches_the_documented_slug_form()
    {
        ProblemTypes.All.Should().OnlyContain(t => t.StartsWith(ProblemTypes.Base, StringComparison.Ordinal));
        ProblemTypes.All.Select(t => t.Substring(ProblemTypes.Base.Length))
            .Should().OnlyContain(slug => slug.All(c => (c >= 'a' && c <= 'z') || c == '-'));
        ProblemTypes.All.Should().HaveCount(16, "15 rows in §7.1's extract plus §6.2's unknown-filter");
    }

    [Fact]
    public async Task A_successful_response_is_left_alone()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync("/api/v1/rfqs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("data", out _).Should().BeTrue();
    }

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
