using System.Text;
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
/// MSP-75/FR-AUD-004: filter/search/export on the global audit log. Row-scoped audit reads already
/// existed (per-aggregate, and a supplier's own trail); this is the staff-facing search across the
/// whole (row-scoped) log, gated by audit.read (only system_admin holds it - Permissions.cs).
///
/// <para><b>Isolation strategy.</b> The Postgres fixture is shared across every integration test in
/// this collection, and ops.audit_log is retained forever (ASM-085) - other tests' rows are always
/// present. Every probe row here carries a per-test-run synthetic AggregateType
/// ("ProbeType_{tag}") and Action ("probe_action_{p|q}_{tag}") that nothing else in the system ever
/// writes, and OccurredAt values fixed in January 2020 - a date no real send or concurrently
/// running test would produce (other seeded probes in this suite use UtcNow-relative timestamps,
/// e.g. AuditPaginationTests). That makes every count below an exact, isolated denominator rather
/// than "some rows came back".</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuditSearchAndExportTests(PostgresApiFixture fixture)
{
    private sealed record AuditEntry(Guid Id, DateTimeOffset OccurredAt, string AggregateType, Guid AggregateId, string Action, string? ActorLabel);
    /// <summary>
    /// Deserialization target for the documented §5.2 list envelope
    /// (<c>{ data, pagination, meta }</c>), which replaced the flat
    /// <c>{ items, hasMore, nextCursor, total }</c> shape.
    /// </summary>
    private sealed record AuditPage(List<AuditEntry> Data, AuditPagination Pagination);

    private sealed record AuditPagination(string Mode, string? NextCursor, string? PrevCursor, int PageSize, int? TotalCount, bool HasMore);

    private static readonly DateTimeOffset Day0 = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day1 = new(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = new(2020, 1, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day3 = new(2020, 1, 4, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day4 = new(2020, 1, 5, 0, 0, 0, TimeSpan.Zero);

    private sealed record ProbeSet(string ProbeType, string ActionP, string ActionQ, Guid AggregateA, Guid AggregateB, Guid ActorX, Guid ActorY);

    /// <summary>
    /// 7 rows total, laid out so every filter dimension and their combination has a known, distinct
    /// expected count:
    ///
    ///   r0: A, X, P, Day0   (outside the [Day1,Day3] date range used below)
    ///   r1: A, X, P, Day1   (date range lower boundary)
    ///   r2: A, X, Q, Day2   (action Q, not P)
    ///   r3: A, Y, P, Day2   (actor Y, not X)
    ///   r4: B, X, P, Day2   (aggregate B, not A)
    ///   r5: A, X, P, Day3   (date range upper boundary)
    ///   r6: A, X, P, Day4   (outside the [Day1,Day3] date range)
    /// </summary>
    private async Task<ProbeSet> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..12];
        var probeType = $"ProbeType_{tag}";
        var actionP = $"probe_action_p_{tag}";
        var actionQ = $"probe_action_q_{tag}";
        var aggregateA = Guid.CreateVersion7();
        var aggregateB = Guid.CreateVersion7();
        var actorX = Guid.CreateVersion7();
        var actorY = Guid.CreateVersion7();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        AuditLog Row(Guid aggregateId, Guid actorId, string action, DateTimeOffset occurredAt) => new()
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = occurredAt,
            ActorKind = AuditActorKind.User,
            ActorUserId = actorId,
            AggregateType = probeType,
            AggregateId = aggregateId,
            Action = action,
            CorrelationId = Guid.CreateVersion7(),
        };

        db.AuditLogs.AddRange(
            Row(aggregateA, actorX, actionP, Day0),
            Row(aggregateA, actorX, actionP, Day1),
            Row(aggregateA, actorX, actionQ, Day2),
            Row(aggregateA, actorY, actionP, Day2),
            Row(aggregateB, actorX, actionP, Day2),
            Row(aggregateA, actorX, actionP, Day3),
            Row(aggregateA, actorX, actionP, Day4));

        await db.SaveChangesAsync();

        return new ProbeSet(probeType, actionP, actionQ, aggregateA, aggregateB, actorX, actorY);
    }

    private static async Task<AuditPage> SearchAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/audit?{query}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuditPage>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private static async Task<List<string>> ExportRowsAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/audit/export?{query}");
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");  // charset is now stated too

        var csv = await response.Content.ReadAsStringAsync();

        // EPIC-19 put a provenance block above the header - the range and filters the file was
        // produced with, stated inside the artefact. Comment lines are dropped here the way a CSV
        // consumer drops them, so these assertions stay about the DATA.
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#') && !line.StartsWith('\uFEFF'))
            .Select(line => line.TrimStart('\uFEFF'))
            .ToArray();
        lines[0].Should().Be("Id,OccurredAt,AggregateType,AggregateId,Action,FromState,ToState,ActorLabel");
        return lines.Skip(1).ToList();
    }

    [Fact]
    public async Task Filter_by_entity_type_alone_returns_exactly_the_rows_under_that_type()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var probe = await SeedAsync();

        var page = await SearchAsync(staff, $"aggregateType={probe.ProbeType}&pageSize=100");

        page.Data.Should().HaveCount(7, "the probe set seeded exactly 7 rows under this synthetic type");
    }

    [Fact]
    public async Task Filter_by_entity_id_narrows_within_the_type_to_that_one_aggregate()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var probe = await SeedAsync();

        var page = await SearchAsync(staff, $"aggregateId={probe.AggregateA}&pageSize=100");

        page.Data.Should().HaveCount(6, "6 of the 7 seeded rows are under aggregate A; " +
            "the 7th (r4) is under aggregate B and must be excluded by id alone, without a type filter");
        page.Data.Should().OnlyContain(i => i.AggregateId == probe.AggregateA);
    }

    [Fact]
    public async Task Filter_by_actor_alone_returns_only_that_actor_s_rows()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var probe = await SeedAsync();

        var page = await SearchAsync(staff, $"actorUserId={probe.ActorX}&pageSize=100");

        page.Data.Should().HaveCount(6, "actor X wrote 6 of the 7 seeded rows; r3 was actor Y");
    }

    [Fact]
    public async Task Filter_by_action_alone_returns_only_that_action_s_rows()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var probe = await SeedAsync();

        var page = await SearchAsync(staff, $"action={probe.ActionP}&pageSize=100");

        page.Data.Should().HaveCount(6, "action P was used on 6 of the 7 seeded rows; r2 was action Q");
    }

    [Fact]
    public async Task Date_range_is_inclusive_on_both_ends()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var probe = await SeedAsync();

        // Full range: [Day1, Day3] should include the boundary rows (Day1, Day3) and the row
        // strictly inside (Day2 x3), excluding Day0 and Day4.
        var fullRange = await SearchAsync(staff,
            $"aggregateType={probe.ProbeType}&from={Uri.EscapeDataString(Day1.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(Day3.ToString("O"))}&pageSize=100");
        fullRange.Data.Should().HaveCount(5, "Day1, three rows at Day2, and Day3 fall inside " +
            "[Day1,Day3] inclusive; Day0 and Day4 do not");

        // Collapsing the range onto exactly one boundary instant proves that instant is INCLUDED,
        // not merely "close to" the edge.
        var exactlyLowerBoundary = await SearchAsync(staff,
            $"aggregateType={probe.ProbeType}&from={Uri.EscapeDataString(Day1.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(Day1.ToString("O"))}&pageSize=100");
        exactlyLowerBoundary.Data.Should().HaveCount(1, "from == to == Day1 must still return the Day1 row");

        var exactlyUpperBoundary = await SearchAsync(staff,
            $"aggregateType={probe.ProbeType}&from={Uri.EscapeDataString(Day3.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(Day3.ToString("O"))}&pageSize=100");
        exactlyUpperBoundary.Data.Should().HaveCount(1, "from == to == Day3 must still return the Day3 row");
    }

    [Fact]
    public async Task Filters_combine_with_AND_semantics_across_all_four_dimensions()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var probe = await SeedAsync();

        // aggregate A, action P, within [Day1,Day3]: r1 (Day1) and r5 (Day3) qualify on all three;
        // r3 also matches A+P+range (actor Y, Day2) - actor is NOT part of this filter combination,
        // so it must be included too.
        var page = await SearchAsync(staff,
            $"aggregateId={probe.AggregateA}&action={probe.ActionP}" +
            $"&from={Uri.EscapeDataString(Day1.ToString("O"))}&to={Uri.EscapeDataString(Day3.ToString("O"))}&pageSize=100");

        page.Data.Should().HaveCount(3,
            "r1, r3, and r5 all match aggregate A AND action P AND the date range; " +
            "r0/r6 fail the range, r2 fails the action, r4 fails the aggregate");

        // Adding the actor narrows it further: only r1 and r5 are also actor X (r3 is actor Y).
        var withActor = await SearchAsync(staff,
            $"aggregateId={probe.AggregateA}&action={probe.ActionP}&actorUserId={probe.ActorX}" +
            $"&from={Uri.EscapeDataString(Day1.ToString("O"))}&to={Uri.EscapeDataString(Day3.ToString("O"))}&pageSize=100");

        withActor.Data.Should().HaveCount(2, "all four dimensions together exclude r3 (actor Y)");
    }

    [Fact]
    public async Task Export_matches_the_filtered_count_not_the_whole_table()
    {
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var probe = await SeedAsync();

        var typeOnlyExport = await ExportRowsAsync(staff, $"aggregateType={probe.ProbeType}");
        typeOnlyExport.Should().HaveCount(7, "the export of the type-only filter must match the " +
            "on-screen count for the same filter (7), not the whole ops.audit_log table");

        var combinationExport = await ExportRowsAsync(staff,
            $"aggregateId={probe.AggregateA}&action={probe.ActionP}" +
            $"&from={Uri.EscapeDataString(Day1.ToString("O"))}&to={Uri.EscapeDataString(Day3.ToString("O"))}");
        combinationExport.Should().HaveCount(3,
            "the export must apply the SAME combined filter as the search endpoint - matching " +
            "Filters_combine_with_AND_semantics_across_all_four_dimensions exactly, not a looser one");
    }

    [Fact]
    public async Task Export_is_not_limited_to_one_page_worth_of_rows()
    {
        // The search endpoint pages at 20 by default - API-ARCHITECTURE.md §6.1's "pageSize
        // default 20", which replaced this endpoint's own former default of 50. Export must not
        // inherit that cap: "everything the filter matches" is the export's contract, not "the
        // current page". 60 rows under one synthetic type is comfortably past the default page size.
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var tag = Guid.NewGuid().ToString("N")[..12];
        var probeType = $"ProbeType_bulk_{tag}";
        var aggregateId = Guid.CreateVersion7();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 60; i++)
            {
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.CreateVersion7(),
                    OccurredAt = Day2.AddSeconds(i),
                    ActorKind = AuditActorKind.System,
                    AggregateType = probeType,
                    AggregateId = aggregateId,
                    Action = "bulk_probe",
                    CorrelationId = Guid.CreateVersion7(),
                });
            }
            await db.SaveChangesAsync();
        }

        var defaultPage = await SearchAsync(staff, $"aggregateType={probeType}");
        defaultPage.Data.Should().HaveCount(20, "the default page size caps the search response");
        defaultPage.Pagination.HasMore.Should().BeTrue();

        var exported = await ExportRowsAsync(staff, $"aggregateType={probeType}");
        exported.Should().HaveCount(60, "export returns everything the filter matches, past the page cap");
    }

    [Fact]
    public async Task Caller_without_audit_read_is_forbidden_from_both_endpoints()
    {
        // ProcurementOfficer holds no audit-related permission (Permissions.cs DefaultPermissions).
        var staff = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);

        var search = await staff.GetAsync("/api/v1/audit");
        search.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var export = await staff.GetAsync("/api/v1/audit/export");
        export.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- EPIC-19 Phase 0: malformed date bounds ------------------------------------------------

    /// <summary>
    /// The bug: <c>?from</c> bound to <c>DateTimeOffset?</c>, so a malformed value bound to NULL -
    /// and a null bound is an ABSENT filter, not a rejected one. The endpoint returned rows OLDER
    /// than the caller asked for, and said nothing.
    ///
    /// <para>Both regression tests assert against a genuinely non-empty unfiltered set, so
    /// "it did not return everything" cannot pass because there was nothing to return.</para>
    /// </summary>
    [Fact]
    public async Task A_malformed_from_with_a_valid_to_is_refused_rather_than_dropped()
    {
        var probes = await SeedAsync();
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        // Control and non-vacuity guard: the unfiltered query really does return rows, so the 422
        // below is the endpoint refusing this bound rather than an empty database answering nothing.
        var unfiltered = await staff.GetFromJsonAsync<AuditPage>(
            $"/api/v1/audit?aggregateType={probes.ProbeType}");
        unfiltered!.Data.Should().NotBeEmpty("control: the endpoint returns rows for this filter");

        var response = await staff.GetAsync(
            $"/api/v1/audit?aggregateType={probes.ProbeType}&from=nonsense&to={Uri.EscapeDataString(Day4.ToString("O"))}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("INVALID_FILTER_VALUE",
            "the same code every other filter guard uses - a new one would be a second vocabulary");
        problem.GetProperty("type").GetString().Should().EndWith("/errors/validation");
        problem.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString())
            .Should().Contain("from");
    }

    [Fact]
    public async Task A_malformed_from_on_its_own_is_refused_as_a_filter_value()
    {
        var probes = await SeedAsync();
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var unfiltered = await staff.GetFromJsonAsync<AuditPage>(
            $"/api/v1/audit?aggregateType={probes.ProbeType}");
        unfiltered!.Data.Should().NotBeEmpty("control: the endpoint answers this filter with rows");

        var response = await staff.GetAsync($"/api/v1/audit?aggregateType={probes.ProbeType}&from=2020-13-45");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "422, not binding's 400: the request is well formed and one filter VALUE is unprocessable");

        // And no list envelope came back - the request failed rather than answering with some other
        // range.
        (await response.Content.ReadAsStringAsync()).Should().NotContain("\"data\"");
    }

    [Fact]
    public async Task A_valid_bound_still_filters()
    {
        // Both directions: the guard must not have turned every date bound into a refusal.
        var probes = await SeedAsync();
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var page = await staff.GetFromJsonAsync<AuditPage>(
            $"/api/v1/audit?aggregateType={probes.ProbeType}&from={Uri.EscapeDataString(Day2.ToString("O"))}");

        page!.Data.Should().NotBeEmpty();
        page.Data.Should().OnlyContain(e => e.OccurredAt >= Day2, "the bound is applied, not merely accepted");
    }

    [Fact]
    public async Task The_export_refuses_a_malformed_bound_too()
    {
        // The list and the export share a filter type and must share its error. An export that
        // answered 400 MALFORMED_JSON where the list answers 422 would be two contracts for one
        // filter.
        var probes = await SeedAsync();
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var control = await staff.GetAsync($"/api/v1/audit/export?aggregateType={probes.ProbeType}");
        control.StatusCode.Should().Be(HttpStatusCode.OK, "control: the export works with no bound at all");

        var response = await staff.GetAsync($"/api/v1/audit/export?aggregateType={probes.ProbeType}&from=nonsense");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().NotBe("text/csv",
            "a refused export must not also emit a CSV body");
    }

    // ---- EPIC-19 Phase 3: the export says what it contains -------------------------------------

    [Fact]
    public async Task The_export_carries_a_BOM_and_states_its_own_filters()
    {
        // An audit CSV is the record of a tender. Detached from the request that produced it, a file
        // with a truncated range is indistinguishable from a complete one - so the range has to be a
        // claim the artefact itself makes.
        var probes = await SeedAsync();
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await staff.GetAsync(
            $"/api/v1/audit/export?aggregateType={probes.ProbeType}&from={Uri.EscapeDataString(Day2.ToString("O"))}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var bytes = await response.Content.ReadAsByteArrayAsync();

        // The BOM, asserted on the BYTES: a string comparison would silently pass without it, and
        // the whole point is what a spreadsheet application sees.
        bytes.Take(3).Should().Equal([(byte)0xEF, (byte)0xBB, (byte)0xBF],
            "Excel reads a BOM-less UTF-8 CSV as the system code page and turns Arabic into mojibake");

        var text = Encoding.UTF8.GetString(bytes);
        text.Should().Contain($"# filter.aggregateType: {probes.ProbeType}");
        text.Should().Contain($"# filter.from: {Day2.ToString("O")}");
        text.Should().Contain("# filter.to: (unbounded)",
            "an absent bound is stated as unbounded rather than omitted - a missing line reads as a missing filter");
        text.Should().Contain("Id,OccurredAt,AggregateType");
    }

    [Fact]
    public async Task The_exports_rows_are_the_same_rows_the_list_returns()
    {
        // The leak this guards: an export that ignores the scope or the filter its list applies. Same
        // filter, same caller - so the two must agree row for row.
        var probes = await SeedAsync();
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var listed = await staff.GetFromJsonAsync<AuditPage>(
            $"/api/v1/audit?aggregateType={probes.ProbeType}&from={Uri.EscapeDataString(Day2.ToString("O"))}&pageSize=100");
        listed!.Data.Should().NotBeEmpty("control: the filter matches something");

        var csv = await (await staff.GetAsync(
            $"/api/v1/audit/export?aggregateType={probes.ProbeType}&from={Uri.EscapeDataString(Day2.ToString("O"))}"))
            .Content.ReadAsStringAsync();

        foreach (var entry in listed.Data)
        {
            csv.Should().Contain(entry.Id.ToString(), "every row the list shows must be in the export");
        }

        // And the other direction: nothing outside the filter leaked into the file.
        var excluded = await staff.GetFromJsonAsync<AuditPage>(
            $"/api/v1/audit?aggregateType={probes.ProbeType}&to={Uri.EscapeDataString(Day1.ToString("O"))}&pageSize=100");
        foreach (var entry in excluded!.Data.Where(e => e.OccurredAt < Day2))
        {
            csv.Should().NotContain(entry.Id.ToString(), "a row outside the range must not be in the file");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // EPIC-19 part 2, Phase 0: the identifier filters, same mechanism as the date bounds.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_malformed_actorUserId_names_the_field_it_could_not_read()
    {
        var probes = await SeedAsync();
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        // Controls in both directions: the filter is real (it narrows) and the endpoint has data.
        // Without these the 422 below could be an endpoint that refuses everything, or an empty
        // table that would look identical either way.
        var unfiltered = await SearchAsync(staff, $"aggregateType={probes.ProbeType}");
        unfiltered.Data.Should().NotBeEmpty("control: the endpoint returns rows unfiltered by actor");

        var narrowed = await SearchAsync(staff, $"aggregateType={probes.ProbeType}&actorUserId={probes.ActorY}");
        narrowed.Data.Should().NotBeEmpty("control: a well-formed actor filter still returns that actor's rows");
        narrowed.Data.Count.Should().BeLessThan(unfiltered.Data.Count,
            "control: the actor filter genuinely narrows, so it is a filter and not a no-op");

        var response = await staff.GetAsync(
            $"/api/v1/audit?aggregateType={probes.ProbeType}&actorUserId=not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "422 naming actorUserId, not binding's 400 MALFORMED_JSON, which names nothing");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("INVALID_FILTER_VALUE");
        problem.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString())
            .Should().Contain("actorUserId");
    }

    [Fact]
    public async Task A_malformed_aggregateId_names_the_field_it_could_not_read()
    {
        var probes = await SeedAsync();
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var unfiltered = await SearchAsync(staff, $"aggregateType={probes.ProbeType}");
        var narrowed = await SearchAsync(staff, $"aggregateType={probes.ProbeType}&aggregateId={probes.AggregateB}");
        narrowed.Data.Should().NotBeEmpty("control: a well-formed aggregate filter returns that aggregate's rows");
        narrowed.Data.Count.Should().BeLessThan(unfiltered.Data.Count, "control: the filter narrows");

        var response = await staff.GetAsync(
            $"/api/v1/audit?aggregateType={probes.ProbeType}&aggregateId=00000000-not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString())
            .Should().Contain("aggregateId");
    }

    [Fact]
    public async Task The_export_refuses_a_malformed_identifier_too()
    {
        // The list and the export share a filter type; until now they did not share its error.
        var probes = await SeedAsync();
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var control = await staff.GetAsync(
            $"/api/v1/audit/export?aggregateType={probes.ProbeType}&actorUserId={probes.ActorY}");
        control.StatusCode.Should().Be(HttpStatusCode.OK, "control: a well-formed actor id exports fine");

        var response = await staff.GetAsync(
            $"/api/v1/audit/export?aggregateType={probes.ProbeType}&actorUserId=not-a-guid");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_malformed_withCount_is_refused_rather_than_silently_omitting_the_total()
    {
        var probes = await SeedAsync();
        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        // Control in both directions: withCount=true produces a total, omitting it leaves the total
        // out. So the parameter does something, and the 422 below is about the value it was given.
        var counted = await SearchAsync(staff, $"aggregateType={probes.ProbeType}&withCount=true");
        counted.Pagination.TotalCount.Should().NotBeNull("control: a well-formed withCount is honoured");

        var uncounted = await SearchAsync(staff, $"aggregateType={probes.ProbeType}");
        uncounted.Pagination.TotalCount.Should().BeNull("control: absent means no total");

        var response = await staff.GetAsync($"/api/v1/audit?aggregateType={probes.ProbeType}&withCount=yes");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString())
            .Should().Contain("withCount");
    }
}
