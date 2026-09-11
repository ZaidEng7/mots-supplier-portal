using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// BRULE-016: the shape that makes the rule expressible, its admin surface, and - since D-59 - what a
/// recorded link now does.
///
/// <para><b>This suite used to assert the opposite.</b> Its fourth test proved that recording a link changed
/// nothing, because the derivation shipped deliberately off pending two decisions: which types attach to
/// which categories, and whether a category-conditioned set reaches suppliers already approved under the flat
/// one. D-59 answers both - on, and retroactive - so that test is now the behaviour test, and it is where the
/// switch had to be acknowledged rather than slipped in.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentTypeCategoryLinkTests(PostgresApiFixture fixture)
{
    /// <summary>system_admin, which holds every permission including reference.manage - the same client the
    /// reference-data suites use, and the persona the screen is behind.</summary>
    private Task<HttpClient> AdminAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    private static async Task<List<string>> LinksForAsync(HttpClient admin, string documentTypeCode)
    {
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/document-type-categories");
        return all.EnumerateArray()
            .First(x => x.GetProperty("documentTypeCode").GetString() == documentTypeCode)
            .GetProperty("categoryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
    }

    [Fact]
    public async Task Every_document_type_appears_even_with_no_links_recorded()
    {
        var admin = await AdminAsync();

        var all = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/document-type-categories");

        // Driven by the document types, not the link rows. An administrator has to be able to see which types
        // they have not classified, and a link-driven list would hide precisely those.
        all.EnumerateArray().Should().NotBeEmpty();
        all.EnumerateArray().Should().Contain(x => x.GetProperty("categoryCodes").GetArrayLength() == 0,
            "a type with no links is the normal state today and must still be listed");
    }

    [Fact]
    public async Task Links_are_recorded_as_a_set_and_a_category_that_does_not_exist_is_refused()
    {
        var admin = await AdminAsync();
        const string type = "chamber_membership";

        // T-073: the links on a SEEDED document type, which the seeder leaves empty and which
        // RequiredDocumentTypeResolver reads to decide who must produce this document. The clear at
        // the end of this test was a last line; a failing assertion above it left chamber_membership
        // narrowed to one category for the rest of the run.
        await using var scoped = new ClearLinks(admin, type);

        var saved = await admin.PutAsJsonAsync($"/api/v1/admin/document-type-categories/{type}",
            new { categoryCodes = new[] { "catering", "transport" } });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await LinksForAsync(admin, type)).Should().BeEquivalentTo(["catering", "transport"]);

        // Whole-set semantics: sending one code REPLACES the pair rather than adding to it, because what an
        // administrator decides is "required for these categories" - one decision, not a sequence.
        await admin.PutAsJsonAsync($"/api/v1/admin/document-type-categories/{type}",
            new { categoryCodes = new[] { "catering" } });
        (await LinksForAsync(admin, type)).Should().BeEquivalentTo(["catering"]);

        // A code that is not a category is refused and named. Left unchecked it would be a requirement no
        // supplier can ever match, invisible until the derivation is switched on - at which point it silently
        // excludes a document from everybody.
        var refused = await admin.PutAsJsonAsync($"/api/v1/admin/document-type-categories/{type}",
            new { categoryCodes = new[] { "catering", "not_a_category" } });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("UNKNOWN_REFERENCE_CODES");
        problem.GetProperty("codes").EnumerateArray().Select(c => c.GetString()).Should().Contain("not_a_category");

        // And the refusal changed nothing - a partially applied set would be worse than a rejection.
        (await LinksForAsync(admin, type)).Should().BeEquivalentTo(["catering"]);

        // An unknown document type is a 404 rather than a silent no-op against nothing.
        (await admin.PutAsJsonAsync("/api/v1/admin/document-type-categories/not_a_type",
            new { categoryCodes = Array.Empty<string>() })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Clearing is how a decision is undone, and it leaves the type listed with an empty set.
        await admin.PutAsJsonAsync($"/api/v1/admin/document-type-categories/{type}",
            new { categoryCodes = Array.Empty<string>() });
        (await LinksForAsync(admin, type)).Should().BeEmpty();
    }

    /// <summary>
    /// What a link does, now that something reads it: it NARROWS a required type to the categories named.
    ///
    /// <para>Both directions in one test, deliberately. A test that only proved the narrowing would pass
    /// just as happily if the resolver returned nothing at all - and "no documents are required of anybody"
    /// is the failure mode this rule was held back for, because it empties the submit gate, the resubmit
    /// gate, the reviewer's approval gate and the completeness figure at once, silently.</para>
    /// </summary>
    [Fact]
    public async Task A_link_narrows_a_required_type_to_the_categories_named()
    {
        var admin = await AdminAsync();
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Links{Guid.NewGuid():N}"[..12]);
        var supplierCode = await supplier.OwnSupplierCodeAsync();

        async Task<List<string>> RequiredCodesAsync()
        {
            var documents = await supplier.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{supplierCode}/documents");
            return documents.EnumerateArray()
                .Where(d => d.GetProperty("isRequired").GetBoolean())
                .Select(d => d.GetProperty("code").GetString()!)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList();
        }

        var linked = await supplier.PostAsJsonAsync("/api/v1/suppliers/me/category-links",
            new { categoryCode = "catering" });
        linked.StatusCode.Should().Be(HttpStatusCode.OK, await linked.Content.ReadAsStringAsync());

        var before = await RequiredCodesAsync();
        before.Should().Contain("commercial_registration",
            "an unlinked required type is required of everyone - that is what keeps an empty link table safe");

        try
        {
            // Linked to a category this caterer does not hold: no longer theirs to provide.
            await admin.PutAsJsonAsync("/api/v1/admin/document-type-categories/commercial_registration",
                new { categoryCodes = new[] { "transport" } });

            (await RequiredCodesAsync()).Should().NotContain("commercial_registration",
                "BRULE-016: a construction supplier and a caterer are not asked for the same paperwork");

            // Linked to a category they DO hold: required again. Same link table, opposite answer - which
            // is what distinguishes a working condition from a resolver that has simply stopped returning
            // anything.
            await admin.PutAsJsonAsync("/api/v1/admin/document-type-categories/commercial_registration",
                new { categoryCodes = new[] { "transport", "catering" } });

            (await RequiredCodesAsync()).Should().Contain("commercial_registration");
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/v1/admin/document-type-categories/commercial_registration",
                new { categoryCodes = Array.Empty<string>() });
        }

        (await RequiredCodesAsync()).Should().BeEquivalentTo(before,
            "clearing the links restores the unconditioned set, so the decision is undoable while it is " +
            "still only a decision");
    }

    [Fact]
    public async Task Only_a_reference_data_manager_may_record_them()
    {
        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.OnboardingReviewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.GetAsync("/api/v1/admin/document-type-categories")).StatusCode
                .Should().Be(HttpStatusCode.Forbidden);
            (await staff.PutAsJsonAsync("/api/v1/admin/document-type-categories/chamber_membership",
                new { categoryCodes = new[] { "catering" } })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Link Outsider Co");
        (await supplier.GetAsync("/api/v1/admin/document-type-categories")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "a supplier must not read which documents apply to whom");

        // Control.
        var admin = await AdminAsync();
        (await admin.GetAsync("/api/v1/admin/document-type-categories")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Puts a seeded document type back to no links - see the T-073 note above.</summary>
    private sealed class ClearLinks(HttpClient admin, string type) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await admin.PutAsJsonAsync($"/api/v1/admin/document-type-categories/{type}",
                new { categoryCodes = Array.Empty<string>() });
    }
}
