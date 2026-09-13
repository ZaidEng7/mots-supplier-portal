// The list rules that govern every list endpoint rather than any one of them: the optional total, unknown filter
// keys, unknown sort keys, and the shape of an empty result.
//
// The audit search carries the whole battery, because it is the only list with a real filter surface, so a filter
// can be ACTIVE while a count is asserted, which is the actual requirement rather than "a count of everything".
//
// The rules are enforced by one shared endpoint filter, so a per-endpoint case pins that every list is actually
// wired to it rather than assuming it from one passing case. Adding a seventh list without wiring it up fails here
// rather than shipping a silently ignoring list.
//
//
// THE COUNT IS ASSERTED UNDER AN ACTIVE FILTER, ON A SMALL PAGE
//
// Both matter. A count of the unfiltered table would pass a naive assertion, and so would a count of the current
// page if the page held everything. Non-matching rows exist under a sibling type to make the filter load-bearing.
//
// The seeded rows sit under a synthetic type nothing else writes, because the audit table is retained forever and
// shared across the collection, so a count is only a real assertion when the denominator is isolated.
//
// And it must be a TOTAL rather than how many rows remain after the cursor. Counting after the keyset condition is
// the natural mistake and looks correct on page one; it only shows up as a total that shrinks as the caller pages.
//
//
// AN UNKNOWN FILTER KEY IS REFUSED RATHER THAN IGNORED
//
// The failure this prevents is specific: a mistyped key bound nothing and returned the whole unfiltered log, which
// looks like a working list.
//
// The control asserts the correctly spelled key still filters, so this is about the unknown key rather than about
// rejecting filters generally.
//
// A request for a numbered page is the highest-value case: the contract defines page mode, no endpoint here serves
// it, and answering with page one of a cursor list would be silently wrong in exactly the way the rule forbids,
// because a caller who asked for page two and got page one has no way to notice.
//
// One case is asserted on a staff persona where a refusal would mean the caller cannot reach the endpoint at all,
// which would make the case vacuous: the filter must run and reject before authorisation is even reached.
//
//
// SORT KEYS, AND THE DOCUMENTED DEFAULT
//
// Only whitelisted keys are accepted, and the whitelisted one is accepted with either direction marker, because the
// direction is part of the request rather than part of the key.
//
// The envelope's own sort field is where the documented default is observable to a client, so it must be populated
// rather than empty.
//
//
// AN EMPTY RESULT IS A LIST, NOT AN ERROR
//
// Asserted on a filter that matches nothing rather than on an empty table, because the tempting wrong answer is a
// not-found for "no such thing", and the envelope must still be well-formed, so the paging and metadata are checked
// too.
//
// And again on a supplier-facing list where empty is the normal first-day state: a supplier with no invitations
// must be told none rather than not-found.

namespace MotsSupplierPortal.Tests.Integration.Contract;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ListQueryConformanceTests(PostgresApiFixture fixture)
{
    private static readonly DateTimeOffset ProbeDay = new(2020, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private async Task<(string MatchingType, string OtherType)> SeedAsync(int matching, int nonMatching)
    {
        var tag = Guid.NewGuid().ToString("N")[..12];
        var matchingType = $"ProbeCount_{tag}";
        var otherType = $"ProbeOther_{tag}";

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        AuditLog Row(string type, int i) => new()
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = ProbeDay.AddSeconds(i),
            ActorKind = AuditActorKind.System,
            AggregateType = type,
            AggregateId = Guid.CreateVersion7(),
            Action = "probe",
            CorrelationId = Guid.CreateVersion7(),
        };

        for (var i = 0; i < matching; i++) db.AuditLogs.Add(Row(matchingType, i));
        for (var i = 0; i < nonMatching; i++) db.AuditLogs.Add(Row(otherType, i));
        await db.SaveChangesAsync();

        return (matchingType, otherType);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task WithCount_returns_the_total_for_the_filtered_set_not_the_page_and_not_the_table()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (matchingType, _) = await SeedAsync(matching: 25, nonMatching: 10);

        var body = await GetJsonAsync(staff, $"/api/v1/audit?aggregateType={matchingType}&pageSize=5&withCount=true");
        var pagination = body.GetProperty("pagination");

        body.GetProperty("data").GetArrayLength().Should().Be(5, "the count must not change how many rows are returned");
        pagination.GetProperty("totalCount").GetInt32().Should().Be(25,
            "the total is over the filtered set - not the page (5), and not the whole retained table");
        pagination.GetProperty("hasMore").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task WithCount_reports_the_same_total_on_a_later_page_as_on_the_first()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (matchingType, _) = await SeedAsync(matching: 12, nonMatching: 0);

        var page1 = await GetJsonAsync(staff, $"/api/v1/audit?aggregateType={matchingType}&pageSize=5&withCount=true");
        var cursor = page1.GetProperty("pagination").GetProperty("nextCursor").GetString();
        var page2 = await GetJsonAsync(staff,
            $"/api/v1/audit?aggregateType={matchingType}&pageSize=5&withCount=true&cursor={Uri.EscapeDataString(cursor!)}");

        page1.GetProperty("pagination").GetProperty("totalCount").GetInt32().Should().Be(12);
        page2.GetProperty("pagination").GetProperty("totalCount").GetInt32().Should().Be(12,
            "a total counted after the cursor predicate would read 7 here");
    }

    [Fact]
    public async Task Without_withCount_the_total_is_null()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (matchingType, _) = await SeedAsync(matching: 3, nonMatching: 0);

        var body = await GetJsonAsync(staff, $"/api/v1/audit?aggregateType={matchingType}");

        body.GetProperty("pagination").GetProperty("totalCount").ValueKind.Should().Be(JsonValueKind.Null,
            "§6.1 omits it unless asked; a count nobody requested is a query nobody should pay for");
    }

    [Fact]
    public async Task WithCount_false_is_treated_as_not_asking()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (matchingType, _) = await SeedAsync(matching: 3, nonMatching: 0);

        var body = await GetJsonAsync(staff, $"/api/v1/audit?aggregateType={matchingType}&withCount=false");

        body.GetProperty("pagination").GetProperty("totalCount").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_unknown_filter_key_is_422_with_the_documented_type_slug()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (matchingType, _) = await SeedAsync(matching: 2, nonMatching: 5);

        var response = await staff.GetAsync($"/api/v1/audit?aggregateTyp={matchingType}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("type").GetString().Should().Be("https://api.mots-portal.sy/errors/unknown-filter");
        problem.GetProperty("status").GetInt32().Should().Be(422);

        var ok = await GetJsonAsync(staff, $"/api/v1/audit?aggregateType={matchingType}");
        ok.GetProperty("data").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Asking_for_page_mode_on_a_cursor_endpoint_is_422_not_silently_page_one()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync("/api/v1/audit?page=2");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("type").GetString().Should().Be("https://api.mots-portal.sy/errors/unknown-filter");
    }

    [Theory]
    [InlineData("/api/v1/audit")]
    [InlineData("/api/v1/suppliers/me/audit")]
    [InlineData("/api/v1/auth/sessions")]
    [InlineData("/api/v1/review/queue")]
    [InlineData("/api/v1/rfqs")]
    [InlineData("/api/v1/suppliers/me/users")]
    public async Task Every_list_endpoint_rejects_an_unknown_filter_key(string path)
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync($"{path}?notAFilter=1");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "every list endpoint must be wired to the shared §6.2 whitelist");
    }

    [Fact]
    public async Task An_unknown_sort_key_is_422()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync("/api/v1/audit?sort=-nonsense");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("UNKNOWN_SORT_KEY");
    }

    [Theory]
    [InlineData("occurredAt")]
    [InlineData("-occurredAt")]
    public async Task A_whitelisted_sort_key_is_accepted(string sort)
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync($"/api/v1/audit?sort={Uri.EscapeDataString(sort)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/v1/audit", "-occurredAt")]
    [InlineData("/api/v1/rfqs", "-createdAt")]
    public async Task The_default_sort_is_reported_in_meta(string path, string expected)
    {
        var client = path.StartsWith("/api/v1/audit")
            ? await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin)
            : (await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"MetaSort {Guid.NewGuid():N}"[..28])).Client;

        var body = await GetJsonAsync(client, path);

        body.GetProperty("meta").GetProperty("sort").GetString().Should().Be(expected);
    }

    [Fact]
    public async Task A_filter_matching_nothing_returns_200_with_an_empty_data_array()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync($"/api/v1/audit?aggregateType=NoSuchType_{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "§5.2: never 404");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetArrayLength().Should().Be(0);
        body.GetProperty("pagination").GetProperty("hasMore").GetBoolean().Should().BeFalse();
        body.GetProperty("pagination").GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
        body.TryGetProperty("meta", out _).Should().BeTrue("the envelope stays well-formed when empty");
    }

    [Fact]
    public async Task A_supplier_with_no_invitations_gets_an_empty_list_not_a_404()
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(
            fixture, $"EmptyList {Guid.NewGuid():N}"[..28]);

        var response = await client.GetAsync("/api/v1/rfqs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Applied_filters_are_echoed_in_meta_and_absent_when_nothing_was_filtered()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (matchingType, _) = await SeedAsync(matching: 1, nonMatching: 0);

        var filtered = await GetJsonAsync(staff, $"/api/v1/audit?aggregateType={matchingType}");
        filtered.GetProperty("meta").GetProperty("filtersApplied").EnumerateArray()
            .Select(e => e.GetString()).Should().ContainSingle().Which.Should().Be($"aggregateType={matchingType}");

        var unfiltered = await GetJsonAsync(staff, "/api/v1/audit?pageSize=1");
        unfiltered.GetProperty("meta").GetProperty("filtersApplied").ValueKind.Should().Be(JsonValueKind.Null,
            "null distinguishes 'nothing was filtered' from 'a filter matched nothing'");
    }
}
