using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>Regression guard for the primary-representative swap race fixed in
/// ManageRepresentativeHandler.SetPrimaryAsync: the "representative" table's partial unique index
/// on (SupplierId) WHERE IsPrimary allows at most one primary per supplier at the database level.
/// Demoting the old primary and promoting the new one in a single SaveChangesAsync risked EF
/// issuing the two UPDATEs in an order the index rejected as a transient duplicate - reproduced by
/// swapping primary back to a previously-demoted representative. The fix commits the demotion in
/// its own SaveChangesAsync before promoting the new primary.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ManageRepresentativeTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Swapping_primary_back_to_a_previously_demoted_representative_succeeds()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Representative Swap Co");

        // Registration seeds one representative ("Integration Tester") as primary.
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

        // Promote the second representative - demotes the original.
        var promoted = await client.PostAsync($"/api/v1/suppliers/me/representatives/{secondRepId}/set-primary", null);
        promoted.StatusCode.Should().Be(HttpStatusCode.OK);

        // Swap back to the now-demoted original primary. Before the fix, this 500'd with
        // "duplicate key value violates unique constraint IX_representative_SupplierId".
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
