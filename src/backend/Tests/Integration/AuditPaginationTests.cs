using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-66: the own-trail audit read is keyset-paged.
///
/// The audit log is append-only and retained indefinitely (ASM-085), so it is the one table
/// guaranteed to grow without bound - which is why it gets keyset rather than offset paging, and
/// why "works on page one" is not sufficient evidence. Offset paging also returns correct rows on
/// page one; it degrades at depth. These tests therefore check behaviour at a boundary and across
/// several pages, and specifically the tie-break case where many rows share a timestamp.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuditPaginationTests(PostgresApiFixture fixture)
{
    private sealed record AuditEntry(Guid Id, DateTimeOffset OccurredAt, string Action);
    private sealed record AuditPage(List<AuditEntry> Items, bool HasMore, string? NextCursor, int? Total);

    private async Task<Guid> SeedTrailAsync(HttpClient client, int rows)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplierId = await db.Suppliers.OrderByDescending(s => s.CreatedAt).Select(s => s.Id).FirstAsync();

        // Deliberately identical timestamps for half the rows. One request already writes several
        // audit rows at the same instant under one correlation id, so ties are the normal case, not
        // an edge case - and a keyset that pages on OccurredAt alone drops or repeats rows here.
        var sharedInstant = DateTimeOffset.UtcNow.AddMinutes(-5);

        for (var i = 0; i < rows; i++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.CreateVersion7(),
                OccurredAt = i % 2 == 0 ? sharedInstant : DateTimeOffset.UtcNow.AddSeconds(-i),
                ActorKind = AuditActorKind.System,
                AggregateType = "Supplier",
                AggregateId = supplierId,
                Action = $"paging_probe_{i:D3}",
                CorrelationId = Guid.CreateVersion7(),
            });
        }

        await db.SaveChangesAsync();
        return supplierId;
    }

    private static async Task<AuditPage> GetPageAsync(HttpClient client, string? cursor, int limit)
    {
        var url = $"/api/v1/suppliers/me/audit?limit={limit}"
            + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuditPage>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    [Fact]
    public async Task Page_is_bounded_and_reports_whether_more_rows_exist()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Audit Paging Co");
        await SeedTrailAsync(client, 30);

        var page = await GetPageAsync(client, null, 10);

        page.Items.Should().HaveCount(10, "the server must enforce the bound, not merely offer it");
        page.HasMore.Should().BeTrue("more rows exist, and a caller that is not told this believes it has everything");
        page.NextCursor.Should().NotBeNullOrEmpty();
        page.Total.Should().BeNull(
            "Total is deliberately absent here - it needs a COUNT over a table retained forever, " +
            "and means little under keyset paging where there is no page count to render");
    }

    [Fact]
    public async Task Second_page_returns_genuinely_different_rows()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Audit Paging Two");
        await SeedTrailAsync(client, 30);

        var first = await GetPageAsync(client, null, 10);
        var second = await GetPageAsync(client, first.NextCursor, 10);

        second.Items.Should().HaveCount(10);
        second.Items.Select(i => i.Id).Should().NotIntersectWith(first.Items.Select(i => i.Id),
            "a cursor that returns overlapping rows is not paging, it is re-reading");
    }

    [Fact]
    public async Task Keyset_walks_the_whole_trail_without_dropping_or_repeating_a_row_at_depth()
    {
        // The test that matters. Page one works under offset paging too; what distinguishes keyset
        // is that it stays correct deep into the set, and stays correct across ties. Half the seeded
        // rows share one timestamp, so a cursor without the Id tie-break loses rows here.
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Audit Paging Depth");
        var supplierId = await SeedTrailAsync(client, 47);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expected = await db.AuditLogs.CountAsync(a => a.AggregateId == supplierId);

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var page = await GetPageAsync(client, cursor, 5);
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
            pages++;
            pages.Should().BeLessThan(50, "a cursor that never advances would loop forever");
        }
        while (cursor is not null);

        seen.Should().HaveCount(expected, "every row must be visited exactly once across the walk");
        seen.Should().OnlyHaveUniqueItems("a row returned on two pages means the cursor is not strict");
        pages.Should().BeGreaterThan(5, "the walk must actually go deep rather than ending on page one");
    }

    [Fact]
    public async Task Limit_is_clamped_so_a_caller_cannot_reinstate_the_unbounded_query()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Audit Paging Clamp");
        await SeedTrailAsync(client, 30);

        var page = await GetPageAsync(client, null, 100_000);

        page.Items.Count.Should().BeLessThanOrEqualTo(200,
            "the bound is enforced server-side; offering a limit a caller can override is not a bound");
    }

    [Fact]
    public async Task A_malformed_cursor_starts_from_the_beginning_rather_than_failing()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Audit Paging Bad Cursor");
        await SeedTrailAsync(client, 30);

        var page = await GetPageAsync(client, "not-a-real-cursor", 10);

        page.Items.Should().HaveCount(10,
            "an invented or truncated cursor is a caller mistake on a list read; answering with " +
            "page one is more useful than a 500 and leaks nothing");
    }
}
