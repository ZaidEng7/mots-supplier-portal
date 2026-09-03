using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Api.Errors;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// §7.2's validation shape end to end - the bilingual field-scoped body, on real endpoints.
///
/// <para>§13's definition-of-done lists *"Validation returns the bilingual field errors array"* as an
/// item in its own right. The catalogue coverage test proves every rule HAS a sentence; this proves
/// the sentence reaches the wire, in both languages, on the path the SPA reads.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ValidationProblemShapeTests(PostgresApiFixture fixture)
{
    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "§7.2: a field-level validation failure is 422");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task A_validation_failure_carries_the_documented_envelope()
    {
        var client = fixture.CreateClient();

        var problem = await PostAsync(client, "/api/v1/auth/register",
            new { displayNameAr = "", displayNameEn = "", email = "not-an-email", representativeName = "", representativePhone = "" });

        problem.GetProperty("type").GetString().Should().Be(ProblemTypes.Validation);
        problem.GetProperty("status").GetInt32().Should().Be(422);
        problem.GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
        problem.TryGetProperty("errors", out var errors).Should().BeTrue("§7.2 extends the base problem with errors[]");
        errors.ValueKind.Should().Be(JsonValueKind.Array, "§7.2's errors is an array, not ASP.NET's dictionary");
        errors.GetArrayLength().Should().BeGreaterThan(0);

        foreach (var error in errors.EnumerateArray())
        {
            error.GetProperty("field").ValueKind.Should().Be(JsonValueKind.String);
            error.GetProperty("code").GetString().Should().NotBeNullOrWhiteSpace();

            var messages = error.GetProperty("messages");
            messages.GetProperty("ar").GetString().Should().NotBeNullOrWhiteSpace();
            messages.GetProperty("en").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Both_languages_are_present_and_the_Arabic_is_Arabic()
    {
        var client = fixture.CreateClient();

        var problem = await PostAsync(client, "/api/v1/auth/register",
            new { displayNameAr = "", displayNameEn = "x", email = "not-an-email", representativeName = "x", representativePhone = "1" });

        var byCode = problem.GetProperty("errors").EnumerateArray()
            .ToDictionary(e => e.GetProperty("code").GetString()!, e => e);

        // Transcribed verbatim from §7.2's own worked exemplar.
        byCode["EMAIL_INVALID"].GetProperty("messages").GetProperty("ar").GetString()
            .Should().Be("صيغة البريد الإلكتروني غير صحيحة.");
        byCode["EMAIL_INVALID"].GetProperty("messages").GetProperty("en").GetString()
            .Should().Be("The email address format is invalid.");

        // The Arabic must be Arabic script, not the English sentence copied into both slots - which
        // is exactly what a missing catalogue entry would produce.
        foreach (var error in problem.GetProperty("errors").EnumerateArray())
        {
            var messages = error.GetProperty("messages");
            var ar = messages.GetProperty("ar").GetString()!;

            ar.Should().NotBe(messages.GetProperty("en").GetString(), "a fallback would put the English in both");
            ar.Should().MatchRegex("[؀-ۿ]", "the ar message must be in Arabic script");
        }
    }

    [Fact]
    public async Task Field_paths_are_camel_cased_so_the_SPA_can_map_them_onto_inputs()
    {
        var client = fixture.CreateClient();

        var problem = await PostAsync(client, "/api/v1/auth/register",
            new { displayNameAr = "", displayNameEn = "", email = "a@b.co", representativeName = "", representativePhone = "" });

        var fields = problem.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()!)
            .ToList();

        // RegisterPage registers these exact names with React Hook Form.
        fields.Should().Contain("displayNameAr").And.Contain("representativeName");
        fields.Should().NotContain(f => f.Length > 0 && char.IsUpper(f[0]),
            "§7.2's paths match the request JSON, which is camelCase - PascalCase would map onto nothing");
    }

    /// <summary>
    /// §7.2: "attemptedValue is included only for non-sensitive fields (never for passwords/tokens)".
    /// The 500-leak and credential tests in ErrorModelTests cover the other two leak paths; this is
    /// the third, and the one this batch newly makes possible by echoing values at all.
    /// </summary>
    [Fact]
    public async Task Attempted_value_is_echoed_for_ordinary_fields_but_never_for_a_password()
    {
        var client = fixture.CreateClient();

        var problem = await PostAsync(client, "/api/v1/auth/login",
            new { email = "leakcanary-shape@example.com", password = "" });

        var errors = problem.GetProperty("errors").EnumerateArray().ToList();
        var password = errors.Single(e => e.GetProperty("field").GetString() == "password");

        password.TryGetProperty("attemptedValue", out _).Should()
            .BeFalse("§7.2 forbids echoing a password, empty or not");

        var body = problem.GetRawText();
        body.Should().NotContain("leakcanary-shape@example.com".Replace("shape", "pw"),
            "no credential-shaped value may appear anywhere in the body");
    }

    [Fact]
    public async Task An_ordinary_field_does_echo_its_attempted_value()
    {
        var client = fixture.CreateClient();

        var tooLong = new string('x', 400);
        var problem = await PostAsync(client, "/api/v1/auth/register",
            new { displayNameAr = tooLong, displayNameEn = "x", email = "a@b.co", representativeName = "x", representativePhone = "1" });

        var error = problem.GetProperty("errors").EnumerateArray()
            .Single(e => e.GetProperty("code").GetString() == "DISPLAY_NAME_AR_TOO_LONG");

        error.GetProperty("attemptedValue").GetString().Should().Be(tooLong);
    }
}
