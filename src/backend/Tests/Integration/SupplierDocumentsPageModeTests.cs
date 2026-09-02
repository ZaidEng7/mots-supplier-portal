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

/// <summary>
/// §12.3's back-office document list - the endpoint documented in full with no implementation until
/// now - and the first use of page mode in this codebase.
///
/// <para>Batch 0.2 established that <c>PaginationEnvelope</c> had no <c>page</c> member and could
/// not serialise §12.3's variant, and deliberately did not build it because no endpoint needed it.
/// This endpoint needs it, so §6.1's page row is implemented here: <c>?page=&amp;pageSize=</c>,
/// <i>"Always returns totalCount"</i>, and the <c>page*pageSize &lt;= 10 000</c> cap that was
/// previously unreachable and therefore untestable.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierDocumentsPageModeTests(PostgresApiFixture fixture)
{
    private async Task<(HttpClient Reviewer, string SupplierCode, int Seeded)> SeededSupplierAsync(int documents)
    {
        var name = $"PageMode {Guid.NewGuid():N}"[..26];
        await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        string code;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
            code = supplier.ReferenceCode;
            var type = await db.DocumentTypes.Where(t => t.IsActive && !t.ExpiryTracked).FirstAsync();

            for (var i = 0; i < documents; i++)
            {
                db.SupplierDocuments.Add(SupplierDocument.CreatePendingScan(
                    supplier.Id, type.Id, version: i + 1, quarantineKey: $"pm/{Guid.NewGuid():N}",
                    originalFileName: $"doc{i}.pdf", contentType: "application/pdf", sizeBytes: 100 + i,
                    uploadedByUserId: Guid.CreateVersion7(), issueDate: null, expiryDate: null,
                    expiryTracked: false, today: DateOnly.FromDateTime(DateTime.UtcNow)));
            }
            await db.SaveChangesAsync();
        }

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);
        return (reviewer, code, documents);
    }

    /// <summary>
    /// §12.3's worked response shape: <c>pagination: { mode: "page", page, pageSize, totalCount,
    /// hasMore }</c>. Asserted as a whole, because "page mode" is precisely the set of keys that
    /// differ from cursor mode.
    /// </summary>
    [Fact]
    public async Task The_list_returns_the_documented_page_mode_envelope()
    {
        var (reviewer, code, seeded) = await SeededSupplierAsync(7);

        var body = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents?page=1&pageSize=3");
        var pagination = body.GetProperty("pagination");

        pagination.GetProperty("mode").GetString().Should().Be("page");
        pagination.GetProperty("page").GetInt32().Should().Be(1);
        pagination.GetProperty("pageSize").GetInt32().Should().Be(3);
        pagination.GetProperty("totalCount").GetInt32().Should().Be(seeded,
            "§6.1 page mode: \"Always returns totalCount\" - and over the filtered set, not the page");
        pagination.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetArrayLength().Should().Be(3);
        body.GetProperty("meta").GetProperty("sort").GetString().Should().Be("-uploadedAt",
            "§12.3's own worked request and response both carry sort=-uploadedAt");
    }

    /// <summary>Offset paging's actual job: page 3 must be different rows from page 1.</summary>
    [Fact]
    public async Task Paging_through_returns_every_document_exactly_once()
    {
        var (reviewer, code, seeded) = await SeededSupplierAsync(7);

        var seen = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var body = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents?page={page}&pageSize=3");
            seen.AddRange(body.GetProperty("data").EnumerateArray().Select(d => d.GetProperty("documentId").GetString()!));
        }

        seen.Should().HaveCount(seeded);
        seen.Should().OnlyHaveUniqueItems("a non-deterministic order under OFFSET repeats and drops rows between pages");
    }

    [Fact]
    public async Task The_last_page_reports_hasMore_false()
    {
        var (reviewer, code, _) = await SeededSupplierAsync(4);

        var body = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents?page=2&pageSize=3");

        body.GetProperty("pagination").GetProperty("hasMore").GetBoolean().Should().BeFalse();
        body.GetProperty("data").GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// §6.1: *"Hard cap `page*pageSize &lt;= 10 000` to protect the DB; beyond that → `422` advising
    /// cursor mode."* Previously unreachable because no endpoint served page mode.
    /// </summary>
    [Theory]
    [InlineData(200, 100)]   // 20 000
    [InlineData(101, 100)]   // 10 100
    public async Task A_page_offset_past_the_documented_cap_is_422(int page, int pageSize)
    {
        var (reviewer, code, _) = await SeededSupplierAsync(1);

        var response = await reviewer.GetAsync($"/api/v1/suppliers/{code}/documents?page={page}&pageSize={pageSize}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("PAGE_OFFSET_TOO_LARGE");
        problem.GetProperty("detail").GetString().Should().Contain("cursor",
            "§6.1 says the 422 advises cursor mode, so the message has to actually say so");
    }

    /// <summary>The boundary itself is allowed - the cap is "&lt;= 10 000", not "&lt; 10 000".</summary>
    [Fact]
    public async Task The_cap_boundary_itself_is_allowed()
    {
        var (reviewer, code, _) = await SeededSupplierAsync(1);

        var response = await reviewer.GetAsync($"/api/v1/suppliers/{code}/documents?page=100&pageSize=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "100 * 100 = 10 000, which the cap permits");
    }

    /// <summary>§6.2's multi-value OR form, which §12.3's own worked request uses.</summary>
    [Fact]
    public async Task The_state_filter_narrows_and_is_echoed_in_meta()
    {
        var (reviewer, code, seeded) = await SeededSupplierAsync(3);

        var body = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents?state=PendingScan");

        body.GetProperty("pagination").GetProperty("totalCount").GetInt32().Should().Be(seeded);
        body.GetProperty("meta").GetProperty("filtersApplied").EnumerateArray()
            .Select(e => e.GetString()).Should().ContainSingle().Which.Should().Be("state=PendingScan");

        var none = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents?state=Approved");
        none.GetProperty("pagination").GetProperty("totalCount").GetInt32().Should().Be(0);
        none.GetProperty("data").GetArrayLength().Should().Be(0, "§5.2: empty results are data: [] with 200");
    }

    /// <summary>
    /// §12.3's documented row fields. documentId is a Guid rather than the "DOC-2026-013377" short
    /// code the document shows - SupplierDocument has no reference code, and minting one is out of
    /// this batch's scope. Reported, and asserted as it actually is so the divergence is visible.
    /// </summary>
    [Fact]
    public async Task Each_row_carries_the_documented_fields()
    {
        var (reviewer, code, _) = await SeededSupplierAsync(1);

        var body = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents");
        var row = body.GetProperty("data").EnumerateArray().Single();

        row.GetProperty("documentId").GetString().Should().NotBeNullOrEmpty();
        row.GetProperty("documentTypeCode").GetString().Should().NotBeNullOrEmpty();
        row.GetProperty("state").GetString().Should().Be(nameof(DocumentState.PendingScan));
        row.GetProperty("downloadUrl").GetString().Should().Contain("/download-url");
        row.GetProperty("uploadedAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
        row.GetProperty("expiryState").ValueKind.Should().Be(JsonValueKind.Null,
            "this type does not track expiry, so it is not in the expiry machine at all - " +
            "\"Valid\" would assert something the schema does not know");
    }

    /// <summary>
    /// The persona split on this one path: a SUPPLIER gets their own onboarding checklist (one row
    /// per document type), a reviewer gets §12.3's paged document list. Same URL, decided by scope.
    /// </summary>
    [Fact]
    public async Task A_supplier_gets_their_checklist_from_the_same_path()
    {
        var name = $"Checklist {Guid.NewGuid():N}"[..26];
        var (supplier, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        string code;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            code = (await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name)).ReferenceCode;
        }

        var body = await supplier.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents");

        body.ValueKind.Should().Be(JsonValueKind.Array,
            "the supplier's own view is the checklist array the onboarding wizard renders, not a paged envelope");
    }
}
