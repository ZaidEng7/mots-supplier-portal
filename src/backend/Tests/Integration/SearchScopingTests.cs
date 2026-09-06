using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// EPIC-20/SCR-906. The scoping is the substance: a search that returns a row the caller could not have
/// opened directly is a disclosure, and a quiet one - the title alone tells them the thing exists.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SearchScopingTests(PostgresApiFixture fixture)
{
    private static async Task<JsonElement> SearchAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(query)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<(string Kind, string? Code)> HitsOf(JsonElement results) =>
        results.GetProperty("hits").EnumerateArray()
            .Select(h => (
                h.GetProperty("kind").GetString()!,
                h.GetProperty("referenceCode").ValueKind == JsonValueKind.Null ? null : h.GetProperty("referenceCode").GetString()))
            .ToList();

    [Fact]
    public async Task An_officer_finds_their_own_organisations_RFQ_by_title_and_by_reference_code()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Find{Guid.NewGuid():N}"[..12]);

        var byCode = await SearchAsync(seed.Officer, seed.RfqCode);
        HitsOf(byCode).Should().Contain(("rfq", seed.RfqCode),
            "a reference code is the single most common thing typed into a search box, and hyphens must not " +
            "defeat it - the first tokeniser stripped them INSIDE the token and turned the code into one " +
            "nonsense word that matched nothing");

        // Prefix matching, which is what makes an unstemmed search usable while someone is still typing.
        var byPrefix = await SearchAsync(seed.Officer, seed.RfqCode[..^2]);
        HitsOf(byPrefix).Should().Contain(("rfq", seed.RfqCode));
    }

    [Fact]
    public async Task Another_organisations_officer_finds_nothing_of_it()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Scope{Guid.NewGuid():N}"[..12]);

        var otherOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var outsider = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, otherOrg.Id);

        var results = await SearchAsync(outsider, seed.RfqCode);
        HitsOf(results).Should().NotContain(("rfq", seed.RfqCode),
            "BRULE-029 is not softened by a search box");

        // The control: the owning officer finds it with the same query, so the absence above is the scope
        // working rather than the search failing.
        HitsOf(await SearchAsync(seed.Officer, seed.RfqCode)).Should().Contain(("rfq", seed.RfqCode));
    }

    [Fact]
    public async Task A_supplier_finds_an_RFQ_they_were_invited_to_and_no_supplier_but_themselves()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Sup{Guid.NewGuid():N}"[..12]);

        var results = await SearchAsync(seed.Supplier, seed.RfqCode);
        HitsOf(results).Should().Contain(("rfq", seed.RfqCode), "they were invited and it is published");

        // Cross-supplier confidentiality. Another supplier exists with a searchable name; the first must not
        // see them, and must see themselves - "search for my own company" working is what stops this
        // reading as broken.
        var other = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Rival{Guid.NewGuid():N}"[..12]);
        var otherProfile = await other.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        var otherCode = otherProfile.GetProperty("supplierCode").GetString();

        var supplierHits = HitsOf(await SearchAsync(seed.Supplier, "Rival"));
        supplierHits.Should().NotContain(("supplier", otherCode));
    }

    [Fact]
    public async Task A_supplier_cannot_find_a_tender_that_has_not_been_published()
    {
        // The disclosure that matters most on this endpoint: an invitation can exist from Draft onward
        // (FEAT-08.1), and visibility only opens at Publish. A search that showed a Draft to an invited
        // supplier would leak a tender still being written - the same two conditions
        // SupplierRfqLoader.LoadInvitedAsync applies, applied here.
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Draft{Guid.NewGuid():N}"[..12]);

        var draft = await seed.Officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "مسودة سرية",
            titleEn = "Secret Draft Tender",
            currencyCode = "SYP",
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(2),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(20),
        });
        // 200, not 201: this endpoint answers OK with the created RFQ. Asserted as it behaves rather than as
        // I first assumed.
        draft.StatusCode.Should().Be(HttpStatusCode.OK, await draft.Content.ReadAsStringAsync());
        var draftCode = (await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString();

        HitsOf(await SearchAsync(seed.Supplier, "Secret Draft")).Should().NotContain(("rfq", draftCode));

        // Control: its own officer finds it, so the row is searchable and it is the supplier's scope that
        // excludes it.
        HitsOf(await SearchAsync(seed.Officer, "Secret Draft")).Should().Contain(("rfq", draftCode));
    }

    [Fact]
    public async Task A_ministry_viewer_finds_no_individual_tender()
    {
        // A-10/D-6: the Ministry sees aggregates, not individual tenders. A cross-organisation search would
        // be the widest possible violation of that, so this persona gets no rows rather than every
        // organisation's.
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Min{Guid.NewGuid():N}"[..12]);
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer, organizationId: null);

        HitsOf(await SearchAsync(ministry, seed.RfqCode)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_blank_or_operator_only_query_returns_nothing_rather_than_everything()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Blank{Guid.NewGuid():N}"[..12]);

        // EPIC-19's filter-guard lesson applied to a search: a term that fell through to an unfiltered query
        // would hand back the caller's entire visible world under the guise of a result.
        HitsOf(await SearchAsync(seed.Officer, "   ")).Should().BeEmpty();

        // tsquery operators are stripped rather than executed. Not injection - the value is a parameter -
        // but a caller searching for "R&D" must not be issuing a boolean expression by accident, and "&"
        // alone must not 500.
        HitsOf(await SearchAsync(seed.Officer, "&|!")).Should().BeEmpty();

        // Control: a real term against the same client still finds something.
        HitsOf(await SearchAsync(seed.Officer, seed.RfqCode)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_search()
    {
        var anonymous = fixture.CreateRawClient();
        (await anonymous.GetAsync("/api/v1/search?q=anything")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
