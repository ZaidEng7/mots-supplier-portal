using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>FEAT-06.3/FR-OFF-004: procurement staff discovering offerings across all suppliers.
/// FEAT-06.4/FR-OFF-005: only Active suppliers' offerings surface here - a supplier suspended
/// after listing an offering must disappear from this search even though the Offering row itself
/// is untouched. FEAT-06.2: attributes round-trip through the real create/search endpoints.</summary>
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

    /// <summary>Registers, verifies, and logs in a supplier, then forces it straight to Active -
    /// the same forced-transition pattern as SupplierLifecycleEndpointTests.ApprovedSupplierAsync,
    /// for the same reason: these tests are about buyer search, not about the onboarding journey.</summary>
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
        // Deliberately NOT forced to Active - a freshly registered supplier starts out of scope
        // for buyer search regardless of what it lists.
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
        // OnboardingReviewer has no OfferingSearch grant - a reviewer is not a procurement actor.
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var response = await reviewer.GetAsync("/api/v1/offerings/search");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_wildcard_in_the_search_box_is_a_character_not_a_pattern()
    {
        // Interpolated raw into $"%{query}%", the caller's own `%` and `_` were LIKE syntax rather
        // than text: `?query=%` matched every row and `a_c` matched "abc". The value was always a
        // parameter, so this is not injection - it is that the caller's string stopped meaning what
        // it says.
        var name = $"Wildcard Search {Guid.NewGuid():N}"[..30];
        var (supplierClient, _) = await ActiveSupplierAsync(name);

        var marker = $"Zqx{Guid.NewGuid():N}"[..12];
        await supplierClient.PostAsJsonAsync("/api/v1/suppliers/me/offerings", ValidPayload($"{marker} Tour"));

        var buyer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);

        // Control: a real substring finds it, so the searching itself works and the negatives below
        // are about wildcards rather than about a search that returns nothing.
        var hit = await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/offerings/search?query={marker}");
        hit.EnumerateArray().Should().ContainSingle(o => o.GetProperty("nameEn").GetString() == $"{marker} Tour");

        // A bare `%` is now a literal percent sign, which nothing here contains - so it matches
        // NOTHING rather than everything.
        var wildcard = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/offerings/search?query=%25");
        wildcard.EnumerateArray().Should().NotContain(o => o.GetProperty("nameEn").GetString() == $"{marker} Tour",
            "'%' is a character the caller typed, not 'match everything'");

        // And `_` is a literal underscore, not "any single character".
        var underscore = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/offerings/search?query={marker[..3]}_{marker[4..]}");
        underscore.EnumerateArray().Should().NotContain(o => o.GetProperty("nameEn").GetString() == $"{marker} Tour",
            "'_' is a character, not a single-character wildcard");
    }

    [Fact]
    public async Task A_literal_percent_in_a_name_is_still_findable()
    {
        // The other direction, and the reason escaping has to handle the escape character first: a
        // supplier whose offering genuinely contains '%' must still be searchable for it.
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
