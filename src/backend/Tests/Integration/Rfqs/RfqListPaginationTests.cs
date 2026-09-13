// Cursor paging on the tender lists, and what a malformed cursor does.
//
// The contract names tenders a cursor-default collection and specifies the envelope.
//
//
// THE WHOLE SET IS ASSERTED, NOT PAGE ONE
//
// The failure keyset paging actually produces is a row repeated or skipped at a page boundary, which a page-one
// assertion cannot see.
//
// Seven rows at a page size of three gives an uneven last page, so an off-by-one at the boundary shows up. The
// final page must close the sequence honestly rather than looping forever.
//
// The page-size rules are asserted as the contract states them: a default, a ceiling, and a warning header when a
// request is clamped. The header's exact wording is not specified anywhere, so this asserts the documented FACT,
// that a header is present and the effective size is the ceiling, rather than pinning wording the contract never
// gave.
//
//
// A MALFORMED CURSOR HAS NO SPECIFIED ANSWER, AND THE SILENCE IS REPORTED RATHER THAN FILLED IN
//
// The error catalogue has no slug for an invalid cursor, and the contract says only that cursors are validated.
//
// Every cursor in this codebase is total: an unparseable token yields page one rather than an error. That is now
// one shared type for every time-ordered list, plus two that key on text instead of a timestamp.
//
// So this pins the property the contract DOES imply, that a hostile token must not reach the database or produce a
// server error, and the choice is recorded as a documented silence rather than resolved by inventing a refusal.

namespace MotsSupplierPortal.Tests.Integration.Rfqs;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class RfqListPaginationTests(PostgresApiFixture fixture)
{
    private static object RfqBasics(string titleEn) => new
    {
        titleAr = "طلب ترقيم", titleEn, descriptionAr = (string?)null, descriptionEn = (string?)null,
        currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
        submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1), submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
        clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
    };

    private async Task<(HttpClient Officer, List<string> ReferenceCodes)> SeedAsync(int count, string prefix)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var codes = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var response = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics($"{prefix} {i:D2}"));
            response.EnsureSuccessStatusCode();
            codes.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!);
        }

        return (officer, codes);
    }

    private static (List<string> Codes, JsonElement Pagination) ReadPage(JsonElement body) =>
        ([.. body.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("referenceCode").GetString()!)],
         body.GetProperty("pagination"));

    [Fact]
    public async Task Paging_through_the_whole_list_returns_every_rfq_exactly_once()
    {
        var (officer, seeded) = await SeedAsync(7, $"Page {Guid.NewGuid():N}"[..12]);

        var collected = new List<string>();
        string? cursor = null;
        var guard = 0;

        do
        {
            var url = cursor is null
                ? "/api/v1/rfqs?pageSize=3"
                : $"/api/v1/rfqs?pageSize=3&cursor={Uri.EscapeDataString(cursor)}";
            var body = await officer.GetFromJsonAsync<JsonElement>(url);
            var (codes, pagination) = ReadPage(body);

            codes.Count.Should().BeLessThanOrEqualTo(3, "a page must never exceed the requested pageSize");
            pagination.GetProperty("mode").GetString().Should().Be("cursor");
            pagination.GetProperty("pageSize").GetInt32().Should().Be(3);

            collected.AddRange(codes);
            cursor = pagination.GetProperty("hasMore").GetBoolean()
                ? pagination.GetProperty("nextCursor").GetString()
                : null;
        }
        while (cursor is not null && ++guard < 10);

        collected.Should().HaveCount(7, "no gaps: every seeded RFQ must appear");
        collected.Should().OnlyHaveUniqueItems("no duplicates: a row must not repeat across a page boundary");
        collected.Should().BeEquivalentTo(seeded, "the paged union must be exactly the seeded set");
    }

    [Fact]
    public async Task The_final_page_reports_hasMore_false_and_a_null_next_cursor()
    {
        var (officer, _) = await SeedAsync(2, $"Fin {Guid.NewGuid():N}"[..12]);

        var body = await officer.GetFromJsonAsync<JsonElement>("/api/v1/rfqs?pageSize=100");
        var pagination = body.GetProperty("pagination");

        pagination.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        pagination.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
        pagination.GetProperty("prevCursor").ValueKind.Should().Be(JsonValueKind.Null, "backward paging is not supported");
        pagination.GetProperty("totalCount").ValueKind.Should().Be(JsonValueKind.Null, "§6.1: totalCount omitted unless ?withCount=true");
        body.GetProperty("meta").GetProperty("sort").GetString().Should().Be("-createdAt");
    }

    [Fact]
    public async Task A_page_size_above_the_documented_maximum_is_clamped_and_warned_about()
    {
        var (officer, _) = await SeedAsync(1, $"Clamp {Guid.NewGuid():N}"[..12]);

        var response = await officer.GetAsync("/api/v1/rfqs?pageSize=5000");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "clamping is not an error - §6.1 says clamped, not rejected");

        response.Headers.TryGetValues("Warning", out var warnings).Should().BeTrue("§6.1 requires a Warning header on clamp");
        warnings!.Should().ContainSingle().Which.Should().Contain("100");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pagination").GetProperty("pageSize").GetInt32()
            .Should().Be(100, "the effective page size is the documented ceiling, not what was asked for");
    }

    [Fact]
    public async Task A_page_size_within_range_is_not_warned_about()
    {
        var (officer, _) = await SeedAsync(1, $"NoWarn {Guid.NewGuid():N}"[..12]);

        var response = await officer.GetAsync("/api/v1/rfqs?pageSize=25");

        response.Headers.TryGetValues("Warning", out _).Should().BeFalse();
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("pagination").GetProperty("pageSize").GetInt32().Should().Be(25);
    }

    [Fact]
    public async Task An_omitted_page_size_uses_the_documented_default_of_twenty()
    {
        var (officer, _) = await SeedAsync(1, $"Def {Guid.NewGuid():N}"[..12]);

        var body = await officer.GetFromJsonAsync<JsonElement>("/api/v1/rfqs");

        body.GetProperty("pagination").GetProperty("pageSize").GetInt32().Should().Be(20);
    }

    [Theory]
    [InlineData("not-base64-at-all!!")]
    [InlineData("YWJjZGVm")]                       // valid base64, wrong contents
    [InlineData("'; DROP TABLE rfq.rfq; --")]
    [InlineData("")]
    public async Task A_malformed_cursor_returns_the_first_page_rather_than_failing(string cursor)
    {
        var (officer, seeded) = await SeedAsync(2, $"Bad {Guid.NewGuid():N}"[..12]);

        var response = await officer.GetAsync($"/api/v1/rfqs?pageSize=50&cursor={Uri.EscapeDataString(cursor)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a bad cursor must never be a 500");
        var (codes, _) = ReadPage(await response.Content.ReadFromJsonAsync<JsonElement>());
        codes.Should().Contain(seeded, "an uninterpretable cursor falls back to the first page");
    }
}
