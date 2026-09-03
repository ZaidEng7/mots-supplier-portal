using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>FEAT-06.1/FR-OFF-001: real Offering CRUD from scratch (EPIC-06 had zero code before
/// this). Covers the story's stated AC1-AC4 (persists linked to valid category+UoM and is
/// audited; deactivation hides but retains; price+currency; invalid category/UoM rejected) plus
/// row-scoping between two different suppliers - one supplier must never see, edit, or deactivate
/// another's offering.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class OfferingTests(PostgresApiFixture fixture)
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
        attributes = (IReadOnlyDictionary<string, string>?)null,
    };

    [Fact]
    public async Task Creating_an_offering_with_flexible_attributes_round_trips_them_on_read()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Offering Attrs Co");
        var attributes = new Dictionary<string, string> { ["capacity"] = "50 ضيف", ["duration"] = "4h" };

        var response = await client.PostAsJsonAsync("/api/v1/suppliers/me/offerings", new
        {
            nameAr = "جولة", nameEn = "Attributed Tour", description = (string?)null,
            categoryCode = "tour_operations", unitOfMeasureCode = "trip",
            priceAmount = (decimal?)null, currencyCode = (string?)null,
            attributes,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("attributes").GetProperty("capacity").GetString().Should().Be("50 ضيف");
        body.GetProperty("attributes").GetProperty("duration").GetString().Should().Be("4h");

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me/offerings");
        var found = list.EnumerateArray().Should().ContainSingle(o => o.GetProperty("nameEn").GetString() == "Attributed Tour").Subject;
        found.GetProperty("attributes").GetProperty("capacity").GetString().Should().Be("50 ضيف");
    }

    [Fact]
    public async Task Creating_an_offering_without_attributes_returns_null_not_an_empty_object()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Offering NoAttrs Co");

        var response = await client.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("attributes").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Creating_an_offering_with_a_valid_category_and_unit_persists_and_is_audited()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Offering Create Co");

        var response = await client.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload());
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("nameEn").GetString().Should().Be("City Tour");
        body.GetProperty("categoryCode").GetString().Should().Be("tour_operations");
        body.GetProperty("unitOfMeasureCode").GetString().Should().Be("trip");
        body.GetProperty("priceAmount").GetDecimal().Should().Be(45.50m);
        body.GetProperty("currencyCode").GetString().Should().Be("USD");
        body.GetProperty("isActive").GetBoolean().Should().BeTrue();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me/offerings");
        list.EnumerateArray().Should().ContainSingle(o => o.GetProperty("nameEn").GetString() == "City Tour");
    }

    [Fact]
    public async Task Creating_with_an_unknown_category_is_rejected_with_a_localized_error_code()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Offering BadCategory Co");

        var response = await client.PostAsJsonAsync("/api/v1/suppliers/me/offerings", new
        {
            nameAr = "خدمة", nameEn = "Service", description = (string?)null,
            categoryCode = "not_a_real_category", unitOfMeasureCode = "trip",
            priceAmount = (decimal?)null, currencyCode = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("INVALID_CATEGORY");
    }

    [Fact]
    public async Task Creating_with_an_unknown_unit_of_measure_is_rejected_with_a_localized_error_code()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Offering BadUoM Co");

        var response = await client.PostAsJsonAsync("/api/v1/suppliers/me/offerings", new
        {
            nameAr = "خدمة", nameEn = "Service", description = (string?)null,
            categoryCode = "tour_operations", unitOfMeasureCode = "not_a_real_unit",
            priceAmount = (decimal?)null, currencyCode = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("INVALID_UNIT_OF_MEASURE");
    }

    [Fact]
    public async Task Updating_an_offering_applies_the_new_values()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Offering Update Co");
        var created = await client.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload());
        var offeringId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/suppliers/me/offerings/{offeringId}", ValidPayload("City Tour (Updated)"));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me/offerings");
        list.EnumerateArray().Should().ContainSingle(o => o.GetProperty("id").GetGuid() == offeringId
            && o.GetProperty("nameEn").GetString() == "City Tour (Updated)");
    }

    [Fact]
    public async Task Deactivating_an_offering_hides_nothing_from_history_but_marks_it_inactive()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Offering Deactivate Co");
        var created = await client.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload());
        var offeringId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var deactivateResponse = await client.PostAsync($"/api/v1/suppliers/me/offerings/{offeringId}/deactivate", null);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // AC2: retained (the row is still there, still returned by list), not deleted.
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me/offerings");
        var found = list.EnumerateArray().Should().ContainSingle(o => o.GetProperty("id").GetGuid() == offeringId).Subject;
        found.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_supplier_cannot_see_edit_or_deactivate_another_supplier_s_offering()
    {
        var ownerClient = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Offering Owner Co");
        var created = await ownerClient.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload());
        var offeringId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var otherClient = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Offering Other Co");

        // Not visible in the other supplier's own list.
        var otherList = await otherClient.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me/offerings");
        otherList.EnumerateArray().Should().NotContain(o => o.GetProperty("id").GetGuid() == offeringId);

        // Cannot edit it - reads as not-found, not forbidden, so the id's existence isn't leaked.
        var updateAttempt = await otherClient.PutAsJsonAsync($"/api/v1/suppliers/me/offerings/{offeringId}", ValidPayload("Hijacked"));
        updateAttempt.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Cannot deactivate it either.
        var deactivateAttempt = await otherClient.PostAsync($"/api/v1/suppliers/me/offerings/{offeringId}/deactivate", null);
        deactivateAttempt.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And it is untouched - still active, still owned by the original supplier.
        var ownerList = await ownerClient.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me/offerings");
        var stillThere = ownerList.EnumerateArray().Should().ContainSingle(o => o.GetProperty("id").GetGuid() == offeringId).Subject;
        stillThere.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }
}
