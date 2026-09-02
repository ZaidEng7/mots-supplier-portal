using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// The parts of API-ARCHITECTURE.md §5.2/§6.1/§6.2/§6.3 that govern every list endpoint rather than
/// any one of them: `?withCount=true`, unknown filter keys, unknown sort keys, and the empty-result
/// shape.
///
/// <para>The audit search endpoint carries the whole battery because it is the only list endpoint
/// with a real filter surface (six dimensions), so a filter can be active while a count is asserted
/// - which is the actual requirement, not "a count of everything". The rules are enforced by one
/// shared endpoint filter (<c>ListQueryFilter</c>), so the per-endpoint theory below pins that every
/// list endpoint is actually wired to it, rather than assuming it from one passing case.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ListQueryConformanceTests(PostgresApiFixture fixture)
{
    private static readonly DateTimeOffset ProbeDay = new(2020, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Seeds <paramref name="matching"/> rows under a synthetic aggregate type nothing else writes,
    /// plus <paramref name="nonMatching"/> rows under a second type. ops.audit_log is retained
    /// forever and shared across this collection, so a count is only a real assertion when the
    /// denominator is isolated like this.
    /// </summary>
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

    // ---- A1: ?withCount=true (§6.1) ------------------------------------------------------------

    /// <summary>
    /// §6.1, cursor row: <i>"`totalCount` omitted unless `?withCount=true`"</i>.
    ///
    /// <para>The count is asserted UNDER AN ACTIVE FILTER and on a page far smaller than the match
    /// set (25 rows, pageSize 5). Both matter: a count of the unfiltered table would pass a naive
    /// assertion, and a count of "the current page" would too if the page held everything. 10
    /// non-matching rows exist under a sibling type to make the filter load-bearing.</para>
    /// </summary>
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

    /// <summary>
    /// The count must be a TOTAL, not "how many rows remain after this cursor". Counting after the
    /// keyset predicate is the natural mistake, and it looks correct on page one - it only shows up
    /// as a total that shrinks as the caller pages.
    /// </summary>
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

    // ---- A3: unknown filter keys (§6.2) --------------------------------------------------------

    /// <summary>
    /// §6.2: <i>"Unknown filter key → `422` (`type: …/errors/unknown-filter`) rather than silent
    /// ignore."</i>
    ///
    /// <para>The failure this prevents is specific: <c>?aggregateTyp=X</c> (a typo) previously bound
    /// nothing and returned the whole unfiltered log, which looks like a working list. The control
    /// below asserts the correctly-spelled key still filters, so this is about the unknown key and
    /// not about rejecting filters generally.</para>
    /// </summary>
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

        // Control: the same request spelled correctly filters rather than 422ing.
        var ok = await GetJsonAsync(staff, $"/api/v1/audit?aggregateType={matchingType}");
        ok.GetProperty("data").GetArrayLength().Should().Be(2);
    }

    /// <summary>
    /// <c>?page=2</c> is the highest-value case: §6.1 defines page mode, no endpoint here serves it,
    /// and answering with page one of a cursor list would be silently wrong in exactly the way §6.2
    /// forbids. A caller who asked for page 2 and got page 1 has no way to notice.
    /// </summary>
    [Fact]
    public async Task Asking_for_page_mode_on_a_cursor_endpoint_is_422_not_silently_page_one()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync("/api/v1/audit?page=2");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("type").GetString().Should().Be("https://api.mots-portal.sy/errors/unknown-filter");
    }

    /// <summary>
    /// The rule is enforced by a shared endpoint filter, which is worth nothing on an endpoint that
    /// forgot to apply it. One case per list endpoint, so adding a seventh list endpoint without
    /// wiring it up fails here rather than shipping a silently-ignoring list.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/audit")]
    [InlineData("/api/v1/suppliers/me/audit")]
    [InlineData("/api/v1/auth/sessions")]
    [InlineData("/api/v1/review/queue")]
    // §12-A/C1: the supplier and buyer RFQ lists converged onto this one route, so there is one
    // row here where there were two - not a dropped case.
    [InlineData("/api/v1/rfqs")]
    [InlineData("/api/v1/suppliers/me/users")]
    public async Task Every_list_endpoint_rejects_an_unknown_filter_key(string path)
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync($"{path}?notAFilter=1");

        // 403 would mean this staff persona cannot reach the endpoint at all, which would make the
        // case vacuous - the filter must run and reject before authorization is even reached here.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "every list endpoint must be wired to the shared §6.2 whitelist");
    }

    // ---- A2: unknown sort keys (§6.3) ----------------------------------------------------------

    /// <summary>§6.3: <i>"Only whitelisted sort keys per endpoint; unknown key → `422`."</i></summary>
    [Fact]
    public async Task An_unknown_sort_key_is_422()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync("/api/v1/audit?sort=-nonsense");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("UNKNOWN_SORT_KEY");
    }

    /// <summary>
    /// The whitelisted key is accepted with either direction marker. §6.3's syntax is
    /// <c>?sort=field</c> / <c>?sort=-field</c>, so the direction is part of the request, not part
    /// of the key being whitelisted.
    /// </summary>
    [Theory]
    [InlineData("occurredAt")]
    [InlineData("-occurredAt")]
    public async Task A_whitelisted_sort_key_is_accepted(string sort)
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync($"/api/v1/audit?sort={Uri.EscapeDataString(sort)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// §6.3: <i>"Default sort documented per endpoint"</i>. The envelope's own `meta.sort` is where
    /// that documentation is observable by a client, so it must be populated rather than null.
    /// </summary>
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

    // ---- A4: empty results (§5.2) --------------------------------------------------------------

    /// <summary>
    /// §5.2: <i>"Empty results return `data: []` with `200`, never `404`."</i>
    ///
    /// <para>Asserted on a filter that matches nothing rather than on an empty table, because the
    /// tempting wrong answer is a 404 for "no such thing" - and the envelope must still be
    /// well-formed, so `pagination` and `meta` are checked too. A caller with a filter that matched
    /// nothing gets a list, not an error.</para>
    /// </summary>
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

    /// <summary>
    /// The same rule on a supplier-facing list, where "empty" is the normal first-day state: a
    /// supplier with no invitations must be told "none", not "not found".
    /// </summary>
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

    // ---- meta.filtersApplied (§5.2) ------------------------------------------------------------

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
