// The primary-representative swap, and the race it used to lose.
//
// The table carries a unique index over primary representatives only, so at most one primary per supplier is
// enforced by the database.
//
// Demoting the old primary and promoting the new one in a single save risked the mapper issuing the two updates in an
// order the index rejected as a momentary duplicate. Reproduced by swapping the primary back to a
// previously-demoted representative, which failed with a duplicate-key error.
//
// The fix commits the demotion in its own save before promoting the new primary, and this is the guard for it.
//
// Registration seeds one representative as primary, so promoting the second demotes the original, and swapping back
// is the case that used to fail.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ManageRepresentativeTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Swapping_primary_back_to_a_previously_demoted_representative_succeeds()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Representative Swap Co");

        var before = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        var originalPrimaryId = before.GetProperty("representatives").EnumerateArray()
            .Single(r => r.GetProperty("isPrimary").GetBoolean())
            .GetProperty("id").GetGuid();

        var added = await client.PostAsJsonAsync("/api/v1/suppliers/me/representatives", new
        {
            fullName = "Second Representative",
            email = "second-rep@example.com",
            phone = "+963900000001",
            position = "Deputy",
        });
        added.StatusCode.Should().Be(HttpStatusCode.OK);
        var addedBody = await added.Content.ReadFromJsonAsync<JsonElement>();
        var secondRepId = addedBody.GetProperty("representatives").EnumerateArray()
            .Single(r => r.GetProperty("email").GetString() == "second-rep@example.com")
            .GetProperty("id").GetGuid();

        var promoted = await client.PostAsync($"/api/v1/suppliers/me/representatives/{secondRepId}/set-primary", null);
        promoted.StatusCode.Should().Be(HttpStatusCode.OK);

        var swappedBack = await client.PostAsync($"/api/v1/suppliers/me/representatives/{originalPrimaryId}/set-primary", null);
        swappedBack.StatusCode.Should().Be(HttpStatusCode.OK,
            "swapping primary back to a previously-demoted representative must not violate the partial unique index");

        var after = await swappedBack.Content.ReadFromJsonAsync<JsonElement>();
        var representatives = after.GetProperty("representatives").EnumerateArray().ToList();

        representatives.Should().ContainSingle(r => r.GetProperty("isPrimary").GetBoolean())
            .Which.GetProperty("id").GetGuid().Should().Be(originalPrimaryId);
        representatives.Single(r => r.GetProperty("id").GetGuid() == secondRepId)
            .GetProperty("isPrimary").GetBoolean().Should().BeFalse();
    }
}
