// Paging the ministry feeds, which their requirements ask for at five hundred rows a page.
//
// THE ASSERTION THAT MATTERS IS THE WALK. A nightly job follows cursors until there are none left, and the two
// ways that goes wrong are both silent: a row that falls between two pages is never loaded and nothing reports
// an error, and a row that appears in both is loaded twice and looks like a duplicate supplier. So the test
// walks the whole feed a page at a time and asserts the result is exactly the set the unpaged representation
// returns - same rows, same count, no repeats. Counting rows alone would pass while the pages were wrong.
//
// A PAGE SIZE OF ONE IS USED FOR THE WALK, because the interesting behaviour is at the boundaries and a
// five-hundred-row default would fit this suite's whole registry in one page and assert nothing. The defaults
// are asserted separately, where they are the subject rather than the vehicle.
//
// THE FEED IS ORDERED BY AN IMMUTABLE KEY, and that is what makes any of this safe. A reference code never
// changes, so a supplier edited between two pages cannot move. Ordering by last-modified - the obvious choice
// for an incremental feed, and the one this nearly used - would let an edited row jump a boundary and be
// skipped. That cannot be asserted directly without racing a write against a page walk, so it is written down
// where the ordering is chosen.
//
// A CURSOR NOBODY ISSUED IS REFUSED rather than treated as "start from the beginning". A nightly job that
// silently restarts on a malformed cursor re-loads the entire registry and reports success, which is the
// failure this refusal exists to make loud.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Application.Exports;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class MinistryFeedPagingTests(PostgresApiFixture fixture)
{
    private static async Task SeedSuppliersAsync(PostgresApiFixture fixture, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Paging Co {Guid.NewGuid():N}"[..24]);
        }
    }

    private static async Task<JsonElement> PageAsync(HttpClient client, string route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<List<string>> WalkAsync(HttpClient client, string route, string key, int limit)
    {
        List<string> seen = [];
        string? cursor = null;

        for (var page = 0; page < 500; page++)
        {
            var url = $"{route}?limit={limit}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var envelope = await PageAsync(client, url);

            seen.AddRange(envelope.GetProperty("data").EnumerateArray().Select(r => r.GetProperty(key).GetString()!));

            var pagination = envelope.GetProperty("pagination");
            if (!pagination.GetProperty("hasMore").GetBoolean())
            {
                pagination.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null,
                    "a feed with nothing left to give must not hand back a cursor to follow");
                return seen;
            }

            cursor = pagination.GetProperty("nextCursor").GetString();
            cursor.Should().NotBeNull("hasMore says there is another page, so there must be a way to ask for it");
        }

        throw new InvalidOperationException("The walk did not terminate, which means hasMore never went false.");
    }

    [Fact]
    public async Task Walking_the_supplier_feed_one_row_at_a_time_returns_every_supplier_exactly_once()
    {
        await SeedSuppliersAsync(fixture, 3);
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var whole = await PageAsync(client, $"/api/v1/feeds/suppliers?limit={FeedPage.MaxLimit}");
        var expected = whole.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("SupplierID").GetString()!).ToList();

        expected.Should().HaveCountGreaterThan(2, "this test needs several pages to cross a boundary");

        var walked = await WalkAsync(client, "/api/v1/feeds/suppliers", "SupplierID", limit: 1);

        walked.Should().Equal(expected,
            "a row that falls between two pages is never loaded and a row in both is loaded twice, "
            + "and neither reports an error to the job doing the loading");
        walked.Distinct().Should().HaveCount(walked.Count, "no supplier may appear on two pages");
    }

    [Fact]
    public async Task Walking_the_rfq_feed_one_row_at_a_time_returns_every_row_exactly_once()
    {
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var whole = await PageAsync(client, $"/api/v1/feeds/rfqs?limit={FeedPage.MaxLimit}");
        var expected = whole.GetProperty("data").EnumerateArray()
            .Select(r => $"{r.GetProperty("RFQNo").GetString()}|{r.GetProperty("SupplierID").GetString()}").ToList();

        if (expected.Count == 0) return;

        List<string> walked = [];
        string? cursor = null;

        for (var page = 0; page < 500; page++)
        {
            var url = "/api/v1/feeds/rfqs?limit=1" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var envelope = await PageAsync(client, url);

            walked.AddRange(envelope.GetProperty("data").EnumerateArray()
                .Select(r => $"{r.GetProperty("RFQNo").GetString()}|{r.GetProperty("SupplierID").GetString()}"));

            if (!envelope.GetProperty("pagination").GetProperty("hasMore").GetBoolean()) break;
            cursor = envelope.GetProperty("pagination").GetProperty("nextCursor").GetString();
        }

        walked.Should().Equal(expected,
            "the pair is the sort key, so a page that ended inside a tender must resume inside it");
        walked.Distinct().Should().HaveCount(walked.Count);
    }

    [Fact]
    public async Task A_caller_that_asks_for_nothing_gets_the_page_size_their_requirements_specify()
    {
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var envelope = await PageAsync(client, "/api/v1/feeds/suppliers");

        envelope.GetProperty("pagination").GetProperty("pageSize").GetInt32().Should().Be(FeedPage.DefaultLimit,
            "their document asks for at least 500 rows a page, so silence means 500 rather than a screen's 20");
    }

    [Theory]
    [InlineData(10_000, FeedPage.MaxLimit)]
    [InlineData(0, FeedPage.DefaultLimit)]
    [InlineData(-5, FeedPage.DefaultLimit)]
    [InlineData(50, 50)]
    public async Task A_page_size_is_clamped_rather_than_refused(int requested, int expected)
    {
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var envelope = await PageAsync(client, $"/api/v1/feeds/suppliers?limit={requested}");

        envelope.GetProperty("pagination").GetProperty("pageSize").GetInt32().Should().Be(expected,
            "a loader asking for an impossible page is asking for something reasonable in a way we cannot "
            + "give it, and an error would leave it with nothing");
    }

    [Theory]
    [InlineData("not-base64-at-all!!")]
    [InlineData("YWJj")]
    public async Task A_cursor_this_feed_did_not_issue_is_refused(string cursor)
    {
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/feeds/rfqs?cursor={Uri.EscapeDataString(cursor)}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a job that silently restarts from row one on a bad cursor re-loads the whole registry and "
            + "reports success");
    }

    [Fact]
    public async Task The_csv_representation_is_not_paged()
    {
        await SeedSuppliersAsync(fixture, 2);
        var client = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var json = await PageAsync(client, $"/api/v1/feeds/suppliers?limit={FeedPage.MaxLimit}");
        var expected = json.GetProperty("data").GetArrayLength();

        using var csv = await client.GetAsync("/api/v1/feeds/suppliers?limit=1");
        csv.EnsureSuccessStatusCode();

        var lines = (await csv.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        (lines - 1).Should().Be(expected,
            "the file download is one file: paging it would help nobody and would quietly truncate the "
            + "export a person clicked");
    }
}
