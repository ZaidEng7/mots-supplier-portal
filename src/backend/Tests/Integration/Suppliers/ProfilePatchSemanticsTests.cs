// Two stacked defects on the profile edit, found in review.
//
// Unknown fields were silently swallowed: a body of nothing but bogus field names answered successfully and
// committed a write.
//
// And the verb had replacement semantics, so any field omitted from the payload was overwritten with nothing, which
// meant a partial update destroyed every field it did not mention while reporting success.
//
// Together those meant a client could send a mistyped field name, be told it succeeded, and have its description
// erased.
//
// The unknown-field case uses the exact payload from the review, and asserts that nothing was written as well as
// that the request was refused.
//
// The partial-update case patches ONE field with everything else absent, and asserts the others individually:
// each of those is what would be empty under replacement semantics.
//
// And the distinction that makes the partial-value type worth having is pinned: an explicit empty value is an
// instruction, and absence is not.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Tests.Integration;

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

        var response = await client.SendAsync(Patch(supplierCode, """
            {"totallyBogusField":"xyz","descriptionEn":"SHOULD-NOT-APPLY"}
            """));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "NFR-SEC-005: an unmodelled field is a client error, not something to swallow");

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

        var response = await client.SendAsync(Patch(supplierCode, """{"description":"UPDATED-DESCRIPTION"}"""));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");

        after.GetProperty("description").GetString().Should().Be("UPDATED-DESCRIPTION",
            "the field that was sent must be applied");

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

        var response = await client.SendAsync(Patch(supplierCode, """{"description":null}"""));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        after.GetProperty("description").ValueKind.Should().Be(JsonValueKind.Null,
            "an explicitly-sent null must clear the field");
        after.GetProperty("website").GetString().Should().Be("https://original.example",
            "clearing one field must not disturb the others");
    }
}
