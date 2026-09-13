// The supplier's own document checklist returns every active document type, not a truncated subset.
//
// This list is not cursor-paged like the other three client-facing lists, and deliberately so. It is a list of
// document TYPES, which are an administrator-managed reference table with a handful of seeded rows and no endpoint
// that could grow it, with at most one latest document attached per type. It is not a list of individually
// uploaded files.
//
// There is no domain method that adds a document type the way the supplier's own methods add profile children, so
// there is no cap to add either.
//
// What stands in for paging is the denominator assertion this project's audit arc has required of every counting
// mechanism: prove the list returns everything the active types describe rather than a silently truncated or
// duplicated subset. Derived from the actual table rather than hand-typed to match a number stated somewhere
// else.
//
// The same query backs BOTH this endpoint and the documents embedded in the reviewer's view, so both consumers are
// asserted here: a regression in the shared query would affect both silently.

namespace MotsSupplierPortal.Tests.Integration.Documents;

using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class OwnDocumentsDenominatorTests(PostgresApiFixture fixture)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Own_documents_returns_exactly_the_active_document_types_no_more_no_fewer()
    {
        int expectedCount;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            expectedCount = await db.DocumentTypes.CountAsync(t => t.IsActive);
        }
        expectedCount.Should().BeGreaterThan(0, "the seeded reference data must not be empty, or this test would pass vacuously");

        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Own Documents Co");
        var res = await client.GetAsync($"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}/documents");
        res.EnsureSuccessStatusCode();

        var documents = await res.Content.ReadFromJsonAsync<List<DocumentTypeStatusDto>>(WebJson);
        documents.Should().NotBeNull();
        documents!.Should().HaveCount(expectedCount, "every active DocumentType must appear exactly once - the list must not be silently truncated or padded");
        documents!.Select(d => d.DocumentTypeId).Should().OnlyHaveUniqueItems();
        documents!.Should().OnlyContain(d => d.LatestDocument == null, "a freshly registered supplier has not uploaded anything yet");
    }

    [Fact]
    public async Task Reviewer_view_shares_the_same_denominator_as_the_supplier_s_own_list()
    {
        var supplierClient = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Reviewer Docs Co");

        string referenceCode;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            referenceCode = (await db.Suppliers.FirstAsync(s => s.LegalInfo!.LegalNameEn.Contains("Reviewer Docs Co"))).ReferenceCode;
        }

        var ownRes = await supplierClient.GetAsync($"/api/v1/suppliers/{await supplierClient.OwnSupplierCodeAsync()}/documents");
        var ownDocuments = await ownRes.Content.ReadFromJsonAsync<List<DocumentTypeStatusDto>>(WebJson);

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var reviewRes = await reviewer.GetAsync($"/api/v1/review/{referenceCode}");
        reviewRes.EnsureSuccessStatusCode();
        var view = await reviewRes.Content.ReadFromJsonAsync<ReviewerSupplierViewDto>(WebJson);

        view.Should().NotBeNull();
        view!.Documents.Should().HaveCount(ownDocuments!.Count,
            "both endpoints call the same BuildAsync - a regression there must be caught from both consumers, not just one");
        view.Documents.Select(d => d.DocumentTypeId).Should()
            .BeEquivalentTo(ownDocuments.Select(d => d.DocumentTypeId), "same underlying set of document types for the same supplier");
    }
}
