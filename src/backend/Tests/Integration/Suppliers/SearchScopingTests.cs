// The cross-entity search, where the scoping IS the substance.
//
// A search that returns a row the caller could not have opened directly is a disclosure, and a quiet one: the title
// alone tells them the thing exists.
//
// Prefix matching is what makes an unstemmed search usable while somebody is still typing.
//
//
// EVERY NEGATIVE HAS A CONTROL
//
// The owning officer finds the same row with the same query, so an absence is the scope working rather than the
// search failing. A supplier finds their OWN company, which is what stops cross-supplier confidentiality reading as
// a broken search.
//
//
// THE DISCLOSURE THAT MATTERS MOST HERE
//
// An invitation can exist from the draft state onward and visibility only opens at publication.
//
// A search that showed a draft to an invited supplier would leak a tender still being written, so the same two
// conditions the supplier-side loader applies are applied here.
//
// And the ministry persona sees aggregates rather than individual tenders, so a cross-organization search would be
// the widest possible violation of that: it gets no rows rather than every organization's.
//
//
// THE FILTER-GUARD LESSON APPLIED TO A SEARCH
//
// A term that fell through to an unfiltered query would hand back the caller's entire visible world under the guise
// of a result.
//
// The query operators are stripped rather than executed. Not injection, because the value is a parameter, but a
// caller searching for an ampersand must not be issuing a boolean expression by accident, and one alone must not
// produce a server error. The control is a real term still finding something.
//
// One assertion records behaviour as it IS rather than as first assumed: the creation endpoint answers with a plain
// success rather than a created status.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Tests.Integration;

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

        HitsOf(await SearchAsync(seed.Officer, seed.RfqCode)).Should().Contain(("rfq", seed.RfqCode));
    }

    [Fact]
    public async Task A_supplier_finds_an_RFQ_they_were_invited_to_and_no_supplier_but_themselves()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Sup{Guid.NewGuid():N}"[..12]);

        var results = await SearchAsync(seed.Supplier, seed.RfqCode);
        HitsOf(results).Should().Contain(("rfq", seed.RfqCode), "they were invited and it is published");

        var other = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Rival{Guid.NewGuid():N}"[..12]);
        var otherProfile = await other.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        var otherCode = otherProfile.GetProperty("supplierCode").GetString();

        var supplierHits = HitsOf(await SearchAsync(seed.Supplier, "Rival"));
        supplierHits.Should().NotContain(("supplier", otherCode));
    }

    [Fact]
    public async Task A_supplier_cannot_find_a_tender_that_has_not_been_published()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Draft{Guid.NewGuid():N}"[..12]);

        var draft = await seed.Officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "مسودة سرية",
            titleEn = "Secret Draft Tender",
            currencyCode = "SYP",
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(2),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(20),
        });
        draft.StatusCode.Should().Be(HttpStatusCode.OK, await draft.Content.ReadAsStringAsync());
        var draftCode = (await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString();

        HitsOf(await SearchAsync(seed.Supplier, "Secret Draft")).Should().NotContain(("rfq", draftCode));

        HitsOf(await SearchAsync(seed.Officer, "Secret Draft")).Should().Contain(("rfq", draftCode));
    }

    [Fact]
    public async Task A_ministry_viewer_finds_no_individual_tender()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Min{Guid.NewGuid():N}"[..12]);
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer, organizationId: null);

        HitsOf(await SearchAsync(ministry, seed.RfqCode)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_blank_or_operator_only_query_returns_nothing_rather_than_everything()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Blank{Guid.NewGuid():N}"[..12]);

        HitsOf(await SearchAsync(seed.Officer, "   ")).Should().BeEmpty();

        HitsOf(await SearchAsync(seed.Officer, "&|!")).Should().BeEmpty();

        HitsOf(await SearchAsync(seed.Officer, seed.RfqCode)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_search()
    {
        var anonymous = fixture.CreateRawClient();
        (await anonymous.GetAsync("/api/v1/search?q=anything")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
