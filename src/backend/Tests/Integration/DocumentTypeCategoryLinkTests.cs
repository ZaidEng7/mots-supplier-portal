using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// BRULE-016's code half: the shape that makes the rule expressible, and its admin surface.
///
/// <para><b>The derivation is deliberately NOT switched on, and these tests assert that too.</b> Recording a
/// link must change nothing about any supplier's required documents until two decisions are made - which types
/// attach to which categories, and whether a tightening reaches suppliers already approved under the flat
/// list. See COMPLETION-INVENTORY.md §4.2.</para>
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

    [Fact]
    public async Task Recording_a_link_changes_no_suppliers_required_documents()
    {
        // The assertion that keeps this half honest. The rule is expressible now and NOT applied, and the
        // difference has to be provable - otherwise "we built the shape" is indistinguishable from "we changed
        // what complete means for every supplier in the system".
        var admin = await AdminAsync();
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Links{Guid.NewGuid():N}"[..12]);
        var supplierCode = await supplier.OwnSupplierCodeAsync();

        async Task<List<(string Name, bool Required)>> RequiredSetAsync()
        {
            var documents = await supplier.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{supplierCode}/documents");
            return documents.EnumerateArray()
                .Select(d => (d.GetProperty("nameEn").GetString()!, d.GetProperty("isRequired").GetBoolean()))
                .OrderBy(x => x.Item1, StringComparer.Ordinal)
                .ToList();
        }

        var before = await RequiredSetAsync();
        before.Should().NotBeEmpty("the reference data must be seeded for this comparison to mean anything");

        // Link a REQUIRED type to a category this supplier does not have. Under the rule as written this
        // document should stop being required for them; the point of this test is that it does not, yet.
        await admin.PutAsJsonAsync("/api/v1/admin/document-type-categories/commercial_registration",
            new { categoryCodes = new[] { "transport" } });

        var after = await RequiredSetAsync();
        after.Should().BeEquivalentTo(before,
            "the links are recorded and not derived from - switching that on needs the two decisions in §4.2");

        await admin.PutAsJsonAsync("/api/v1/admin/document-type-categories/commercial_registration",
            new { categoryCodes = Array.Empty<string>() });
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
}
