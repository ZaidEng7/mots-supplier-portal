using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-84 (4/4): GET /api/v1/suppliers/me/documents is not cursor-paginated like the other three
/// client-facing lists. It is a list of DocumentTypes (an admin-managed reference table, 3 seeded
/// rows, no CRUD endpoint that could grow it) with at most one LatestDocument attached per type -
/// not a list of individually uploaded files. There is no domain method that adds a document
/// TYPE the way Supplier's Add* methods add profile-child rows, so there is no cap to add either;
/// what this PR adds instead is the denominator assertion this project's audit arc has required of
/// every counting/scanning mechanism: prove the list returns everything active DocumentTypes
/// describes, not a silently truncated or duplicated subset - derived from the actual table, not
/// hand-typed to match a number stated anywhere else.
///
/// ListSupplierDocumentsHandler.BuildAsync backs BOTH this endpoint and the reviewer's embedded
/// Documents array inside ReviewerSupplierViewDto (Infrastructure/Suppliers/
/// ReviewApplicationHandlers.cs) - both consumers are asserted here since a regression in the
/// shared query would affect both silently.
/// </summary>
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
