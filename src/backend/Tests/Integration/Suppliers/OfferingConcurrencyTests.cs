// A catalogue entry under the concurrency contract.
//
// This is the aggregate where the missing version column actually bit. A supplier's catalogue is editable by every
// user at that supplier, so two people editing one entry is the ordinary case rather than the exotic one, and until
// this the second write silently overwrote the first, with no error and no trace that anything was lost.
//
//
// IT USES THE RAW CLIENT, AND THAT IS LOAD-BEARING
//
// The suite's default client attaches a current precondition to every mutation so that three hundred older tests
// keep passing, and a caller that always sends a current version can neither observe a stale one nor be missing a
// header.
//
// A test about the precondition has to send exactly what it says it sends. The first version of this class used the
// default client and asserted the missing-header refusal; it got the stale-version refusal from the DATABASE
// instead, because the handler had quietly supplied one.
//
// The version is formatted by the interface's OWN encoder rather than hand-rolled here, because a test that
// reimplements the format proves the test agrees with itself rather than that the caller's version is accepted.
//
//
// THE CONTROL COMES FIRST
//
// A version check that refuses EVERYONE passes every conflict test ever written.
//
// So the happy path is asserted first and against STORAGE, so the edit actually landed. And in the conflict case
// the first writer's edit is what is in the database: asserting only the refusal would pass on an implementation
// that refused the write AND corrupted the row.
//
// The missing-header case has its own control, the same request WITH the header succeeding, so the refusal is about
// the precondition rather than about the payload.
//
//
// THE READ THIS ADDED EXISTS SO THE CONTRACT IS USABLE
//
// A precondition is only usable if a caller can OBTAIN one. This aggregate had a list and no single read, so
// requiring a precondition on deactivation refused every caller, including the suite's own attaching client, which
// is how it was caught.
//
// The version that read issues is asserted accepted by the write, which is the whole point of the pair.
//
// And that read is a direct object read, so it gets the same scoping every other one here has: not-found rather than
// forbidden, resolved by a condition in the query rather than a check afterwards, with an owner control.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class OfferingConcurrencyTests(PostgresApiFixture fixture)
{
    private static object ValidPayload(string nameEn = "City Tour") => new
    {
        nameAr = "جولة في المدينة",
        nameEn,
        description = "Half-day guided city tour",
        categoryCode = "tour_operations",
        unitOfMeasureCode = "trip",
        priceAmount = 45.50m,
        currencyCode = "USD",
        attributes = (Dictionary<string, string>?)null,
    };

    private async Task<(HttpClient Client, Guid OfferingId, string ETag)> OfferingAsync(string name)
    {
        var authenticated = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);

        var client = await SupplierTestClient.CloneWithoutETagsAsync(fixture, authenticated);

        var created = await client.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload());
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var offeringId = body.GetProperty("id").GetGuid();

        var etag = ETag.ForPrecondition((uint)body.GetProperty("rowVersion").GetInt64());

        return (client, offeringId, etag);
    }

    private static async Task<HttpResponseMessage> UpdateAsync(HttpClient client, Guid offeringId, string etag, string newName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/suppliers/me/offerings/{offeringId}")
        {
            Content = JsonContent.Create(ValidPayload(newName)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task A_single_writer_still_succeeds()
    {
        var (client, offeringId, etag) = await OfferingAsync($"OfferConc One {Guid.NewGuid():N}"[..30]);

        var response = await UpdateAsync(client, offeringId, etag, "Renamed Tour");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Offerings.AsNoTracking().FirstAsync(o => o.Id == offeringId))
            .NameEn.Should().Be("Renamed Tour");
    }

    [Fact]
    public async Task Two_writers_from_one_read_lose_the_second_write_rather_than_the_first()
    {
        var (client, offeringId, etag) = await OfferingAsync($"OfferConc Two {Guid.NewGuid():N}"[..30]);

        var first = await UpdateAsync(client, offeringId, etag, "First Writer Wins");
        first.StatusCode.Should().Be(HttpStatusCode.OK, "the first writer had a current version");

        var second = await UpdateAsync(client, offeringId, etag, "Second Writer Overwrites");

        second.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed,
            "the row moved under them - this is the write that used to succeed silently");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Offerings.AsNoTracking().FirstAsync(o => o.Id == offeringId))
            .NameEn.Should().Be("First Writer Wins");
    }

    [Fact]
    public async Task An_edit_with_no_If_Match_is_refused_rather_than_applied()
    {
        var (client, offeringId, etag) = await OfferingAsync($"OfferConc None {Guid.NewGuid():N}"[..30]);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/suppliers/me/offerings/{offeringId}")
        {
            Content = JsonContent.Create(ValidPayload("No Precondition")),
        };
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired, await response.Content.ReadAsStringAsync());

        (await UpdateAsync(client, offeringId, etag, "With Precondition")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_single_offering_read_issues_the_ETag_its_writes_demand()
    {
        var authenticated = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"OfferGet {Guid.NewGuid():N}"[..30]);
        var client = await SupplierTestClient.CloneWithoutETagsAsync(fixture, authenticated);

        var created = await client.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload());
        var offeringId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var read = await client.GetAsync($"/api/v1/suppliers/me/offerings/{offeringId}");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        read.Headers.ETag.Should().NotBeNull("the read has to issue the version its writes demand");

        var update = await UpdateAsync(client, offeringId, read.Headers.ETag!.ToString(), "Read Then Written");
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Another_suppliers_offering_is_not_readable_by_id()
    {
        var mineAuth = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"OfferMine {Guid.NewGuid():N}"[..30]);
        var mine = await SupplierTestClient.CloneWithoutETagsAsync(fixture, mineAuth);
        var created = await mine.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload());
        var offeringId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await mine.GetAsync($"/api/v1/suppliers/me/offerings/{offeringId}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var otherAuth = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"OfferOther {Guid.NewGuid():N}"[..30]);
        var other = await SupplierTestClient.CloneWithoutETagsAsync(fixture, otherAuth);

        (await other.GetAsync($"/api/v1/suppliers/me/offerings/{offeringId}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "§9.2: 404, not 403");
    }
}
