using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Regression guard for the two stacked defects found in review 2026-08-28 on
/// <c>PATCH /api/v1/suppliers/me/profile</c>:
///
/// 1. Unknown fields were silently swallowed - a body of nothing but bogus field names returned
///    200 and committed a write.
/// 2. The verb had PUT semantics - any field omitted from the payload was overwritten with null,
///    so a partial update destroyed every field it did not mention while reporting success.
///
/// Together those meant a client could send a typo'd field name, be told it succeeded, and have
/// its profile description erased.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProfilePatchSemanticsTests(PostgresApiFixture fixture)
{
    private static HttpRequestMessage Patch(string supplierCode, string rawJson) =>
        new(HttpMethod.Patch, $"/api/v1/suppliers/{supplierCode}")
        {
            Content = new StringContent(rawJson, Encoding.UTF8, "application/json"),
        };

    private static async Task SeedProfileAsync(HttpClient client)
    {
        var supplierCode = await client.OwnSupplierCodeAsync();
        var seed = await client.SendAsync(Patch(supplierCode, """
            {"description":"ORIGINAL-DESCRIPTION","website":"https://original.example",
             "supplierGroup":"ORIGINAL-GROUP","currencyCode":"SYP"}
            """));
        seed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_fields_are_rejected_rather_than_silently_ignored()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Patch Semantics Co");
        await SeedProfileAsync(client);
        var supplierCode = await client.OwnSupplierCodeAsync();

        // The exact payload from the review: entirely unknown field names.
        var response = await client.SendAsync(Patch(supplierCode, """
            {"totallyBogusField":"xyz","descriptionEn":"SHOULD-NOT-APPLY"}
            """));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "NFR-SEC-005: an unmodelled field is a client error, not something to swallow");

        // And critically, nothing was written.
        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        after.GetProperty("description").GetString().Should().Be("ORIGINAL-DESCRIPTION",
            "a rejected request must not have committed a write");
    }

    [Fact]
    public async Task Omitted_fields_are_left_untouched_rather_than_wiped()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Patch Partial Co");
        await SeedProfileAsync(client);
        var supplierCode = await client.OwnSupplierCodeAsync();

        // Patch ONE field. Everything else is absent from the body.
        var response = await client.SendAsync(Patch(supplierCode, """{"description":"UPDATED-DESCRIPTION"}"""));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");

        after.GetProperty("description").GetString().Should().Be("UPDATED-DESCRIPTION",
            "the field that was sent must be applied");

        // These are the assertions that fail under PUT-semantics: each would be null.
        after.GetProperty("website").GetString().Should().Be("https://original.example",
            "a field absent from a PATCH body must be left untouched");
        after.GetProperty("supplierGroup").GetString().Should().Be("ORIGINAL-GROUP",
            "a field absent from a PATCH body must be left untouched");
        after.GetProperty("defaultCurrency").GetString().Should().Be("SYP",
            "a field absent from a PATCH body must be left untouched");
    }

    [Fact]
    public async Task Explicit_null_still_clears_a_field()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Patch Null Co");
        await SeedProfileAsync(client);
        var supplierCode = await client.OwnSupplierCodeAsync();

        // The distinction that makes Patch<T> worth having: null is an instruction, absence is not.
        var response = await client.SendAsync(Patch(supplierCode, """{"description":null}"""));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        after.GetProperty("description").ValueKind.Should().Be(JsonValueKind.Null,
            "an explicitly-sent null must clear the field");
        after.GetProperty("website").GetString().Should().Be("https://original.example",
            "clearing one field must not disturb the others");
    }
}
