// Which categories a document type is required for: the shape, the administration surface, and what a recorded
// link now does.
//
//
// THIS SUITE USED TO ASSERT THE OPPOSITE
//
// One test proved that recording a link changed NOTHING, because the narrowing shipped deliberately off pending
// two decisions: which types attach to which categories, and whether a category-conditioned set reaches suppliers
// already approved under the flat one.
//
// A recorded decision answers both, on and retroactive, so that test is now the behaviour test, and it is where
// the switch had to be acknowledged rather than slipped in.
//
//
// WHAT A LINK DOES, ASSERTED IN BOTH DIRECTIONS IN ONE TEST
//
// It NARROWS a required type to the categories named. Linked to a category the supplier does not hold, the
// document is no longer theirs to provide; linked to one they do, it is required again.
//
// Same link table, opposite answer, which is what distinguishes a working condition from a resolver that has
// simply stopped returning anything.
//
// Deliberately both in one test, because a test that only proved the narrowing would pass just as happily if the
// resolver returned nothing at all. "No documents are required of anybody" is the failure mode this rule was held
// back for, because it empties the submit gate, the resubmit gate, the reviewer's approval gate and the
// completeness figure at once, silently.
//
//
// THE ADMINISTRATION SURFACE
//
// The list is driven by the document types rather than the link rows, because an administrator has to be able to
// see which types they have not classified, and a link-driven list would hide precisely those.
//
// Whole-set semantics: sending one code REPLACES the pair rather than adding to it, because what an administrator
// decides is "required for these categories", which is one decision rather than a sequence.
//
// A code that is not a category is refused and named. Left unchecked it would be a requirement no supplier can
// ever match, invisible until the narrowing is switched on, at which point it silently excludes a document from
// everybody. And the refusal changes nothing, because a partially applied set would be worse than a rejection.
//
// An unknown document type is a not-found rather than a silent no-op against nothing, and clearing is how a
// decision is undone, leaving the type listed with an empty set.
//
//
// THE CLEANUP IS NOT A LAST LINE
//
// The links it writes are on a SEEDED type, which the seeder leaves empty and which the required-document resolver
// reads to decide who must produce that document.
//
// Clearing them used to be a last line, so a failing assertion above it left that type narrowed to one category
// for the rest of the run.

namespace MotsSupplierPortal.Tests.Integration.Documents;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentTypeCategoryLinkTests(PostgresApiFixture fixture)
{
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

        all.EnumerateArray().Should().NotBeEmpty();
        all.EnumerateArray().Should().Contain(x => x.GetProperty("categoryCodes").GetArrayLength() == 0,
            "a type with no links is the normal state today and must still be listed");
    }

    [Fact]
    public async Task Links_are_recorded_as_a_set_and_a_category_that_does_not_exist_is_refused()
    {
        var admin = await AdminAsync();
        const string type = "chamber_membership";

        await using var scoped = new ClearLinks(admin, type);

        var saved = await admin.PutAsJsonAsync($"/api/v1/admin/document-type-categories/{type}",
            new { categoryCodes = new[] { "catering", "transport" } });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await LinksForAsync(admin, type)).Should().BeEquivalentTo(["catering", "transport"]);

        await admin.PutAsJsonAsync($"/api/v1/admin/document-type-categories/{type}",
            new { categoryCodes = new[] { "catering" } });
        (await LinksForAsync(admin, type)).Should().BeEquivalentTo(["catering"]);

        var refused = await admin.PutAsJsonAsync($"/api/v1/admin/document-type-categories/{type}",
            new { categoryCodes = new[] { "catering", "not_a_category" } });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("UNKNOWN_REFERENCE_CODES");
        problem.GetProperty("codes").EnumerateArray().Select(c => c.GetString()).Should().Contain("not_a_category");

        (await LinksForAsync(admin, type)).Should().BeEquivalentTo(["catering"]);

        (await admin.PutAsJsonAsync("/api/v1/admin/document-type-categories/not_a_type",
            new { categoryCodes = Array.Empty<string>() })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await admin.PutAsJsonAsync($"/api/v1/admin/document-type-categories/{type}",
            new { categoryCodes = Array.Empty<string>() });
        (await LinksForAsync(admin, type)).Should().BeEmpty();
    }

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
            await admin.PutAsJsonAsync("/api/v1/admin/document-type-categories/commercial_registration",
                new { categoryCodes = new[] { "transport" } });

            (await RequiredCodesAsync()).Should().NotContain("commercial_registration",
                "BRULE-016: a construction supplier and a caterer are not asked for the same paperwork");

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

        var admin = await AdminAsync();
        (await admin.GetAsync("/api/v1/admin/document-type-categories")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class ClearLinks(HttpClient admin, string type) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await admin.PutAsJsonAsync($"/api/v1/admin/document-type-categories/{type}",
                new { categoryCodes = Array.Empty<string>() });
    }
}
