// Procurement staff discovering catalogue entries across every supplier.
//
// Only an ACTIVE supplier's entries surface. A supplier suspended after listing one must disappear from this search
// even though the entry row itself is untouched. A freshly registered supplier is deliberately left inactive, so it
// starts out of scope regardless of what it lists.
//
// Free-form attributes round-trip through the real create and search endpoints.
//
// And a reviewer is refused, because a reviewer is not a procurement actor.
//
//
// THE WILDCARD ESCAPING, IN BOTH DIRECTIONS
//
// Interpolated raw, the caller's own wildcard characters were pattern syntax rather than text: one matched every
// row, and a single-character wildcard in the middle of a word matched neighbours. The value was always a parameter,
// so this is not injection; it is that the caller's string stopped meaning what it says.
//
// A bare wildcard is now a literal character, which nothing here contains, so it matches NOTHING rather than
// everything.
//
// And the other direction, which is the reason escaping has to handle the escape character first: a supplier whose
// entry genuinely contains a wildcard character must still be searchable for it.
//
// The control is a real substring finding the entry, so the searching itself works and the negatives are about
// wildcards rather than about a search that returns nothing.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class OfferingBuyerSearchTests(PostgresApiFixture fixture)
{
    private static object ValidPayload(string nameEn, IReadOnlyDictionary<string, string>? attributes = null) => new
    {
        nameAr = "جولة في المدينة",
        nameEn,
        description = "Half-day guided city tour",
        categoryCode = "tour_operations",
        unitOfMeasureCode = "trip",
        priceAmount = 45.50m,
        currencyCode = "USD",
        attributes,
    };

    private async Task<(HttpClient Client, string ReferenceCode)> ActiveSupplierAsync(string name)
    {
        var (client, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));

        return (client, supplier.ReferenceCode);
    }

    [Fact]
    public async Task An_active_supplier_s_offering_appears_in_buyer_search_with_attributes_intact()
    {
        var name = $"Buyer Search Active {Guid.NewGuid():N}"[..30];
        var (supplierClient, _) = await ActiveSupplierAsync(name);
        var attributes = new Dictionary<string, string> { ["capacity"] = "50 guests", ["language"] = "AR/EN" };
        await supplierClient.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload("Active Co Tour", attributes));

        var buyer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var results = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/offerings/search");

        var found = results.EnumerateArray().Should()
            .ContainSingle(o => o.GetProperty("nameEn").GetString() == "Active Co Tour").Subject;
        found.GetProperty("attributes").GetProperty("capacity").GetString().Should().Be("50 guests");
        found.GetProperty("attributes").GetProperty("language").GetString().Should().Be("AR/EN");
    }

    [Fact]
    public async Task A_non_active_supplier_s_offering_does_not_appear_in_buyer_search()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Buyer Search NonActive {Guid.NewGuid():N}"[..30]);
        await client.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload("NonActive Co Tour"));

        var buyer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var results = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/offerings/search");

        results.EnumerateArray().Should().NotContain(o => o.GetProperty("nameEn").GetString() == "NonActive Co Tour");
    }

    [Fact]
    public async Task Suspending_a_supplier_removes_its_offerings_from_buyer_search()
    {
        var name = $"Buyer Search Suspend {Guid.NewGuid():N}"[..30];
        var (supplierClient, referenceCode) = await ActiveSupplierAsync(name);
        await supplierClient.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload("SuspendMe Co Tour"));

        var buyer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var before = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/offerings/search");
        before.EnumerateArray().Should().Contain(o => o.GetProperty("nameEn").GetString() == "SuspendMe Co Tour");

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var suspend = await reviewer.PostAsJsonAsync($"/api/v1/review/{referenceCode}/suspend", new { reason = "Sanctions screening hit" });
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/offerings/search");
        after.EnumerateArray().Should().NotContain(o => o.GetProperty("nameEn").GetString() == "SuspendMe Co Tour");
    }

    [Fact]
    public async Task A_deactivated_offering_does_not_appear_even_for_an_active_supplier()
    {
        var name = $"Buyer Search DeactOff {Guid.NewGuid():N}"[..30];
        var (supplierClient, _) = await ActiveSupplierAsync(name);
        var created = await supplierClient.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload("DeactOff Co Tour"));
        var offeringId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await supplierClient.PostAsync($"/api/v1/suppliers/me/offerings/{offeringId}/deactivate", null);

        var buyer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var results = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/offerings/search");

        results.EnumerateArray().Should().NotContain(o => o.GetProperty("nameEn").GetString() == "DeactOff Co Tour");
    }

    [Fact]
    public async Task Buyer_search_is_forbidden_without_the_offering_search_permission()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var response = await reviewer.GetAsync("/api/v1/offerings/search");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_wildcard_in_the_search_box_is_a_character_not_a_pattern()
    {
        var name = $"Wildcard Search {Guid.NewGuid():N}"[..30];
        var (supplierClient, _) = await ActiveSupplierAsync(name);

        var marker = $"Zqx{Guid.NewGuid():N}"[..12];
        await supplierClient.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload($"{marker} Tour"));

        var buyer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);

        var hit = await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/offerings/search?query={marker}");
        hit.EnumerateArray().Should().ContainSingle(o => o.GetProperty("nameEn").GetString() == $"{marker} Tour");

        var wildcard = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/offerings/search?query=%25");
        wildcard.EnumerateArray().Should().NotContain(o => o.GetProperty("nameEn").GetString() == $"{marker} Tour",
            "'%' is a character the caller typed, not 'match everything'");

        var underscore = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/offerings/search?query={marker[..3]}_{marker[4..]}");
        underscore.EnumerateArray().Should().NotContain(o => o.GetProperty("nameEn").GetString() == $"{marker} Tour",
            "'_' is a character, not a single-character wildcard");
    }

    [Fact]
    public async Task A_literal_percent_in_a_name_is_still_findable()
    {
        var name = $"Percent Search {Guid.NewGuid():N}"[..30];
        var (supplierClient, _) = await ActiveSupplierAsync(name);

        var marker = $"Pct{Guid.NewGuid():N}"[..10];
        await supplierClient.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload($"{marker} 50% Discount Tour"));

        var buyer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var results = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/offerings/search?query={Uri.EscapeDataString($"{marker} 50%")}");

        results.EnumerateArray().Should().ContainSingle(
            o => o.GetProperty("nameEn").GetString() == $"{marker} 50% Discount Tour",
            "escaping must make '%' searchable, not unsearchable");
    }
}
