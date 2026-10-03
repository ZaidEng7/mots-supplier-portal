// The security section of the administrator's dashboard, through the route: the seven events counted over 24 hours
// and 7 days, the day those counts start from, whether an event is spiking, the ten latest sensitive changes with the
// name of whoever made each, and the section hidden from a viewer without audit.read.
//
//
// THE AUDIT TABLE IS SHARED AND APPEND-ONLY, SO EVERY FIGURE IS A DIFFERENCE OR A ROW OF OUR OWN
//
// Every class in the run writes audit rows and none can delete them, so the counts are asserted as the change
// between two readings around the rows a test seeds, and the lists are asserted on the ids of the rows it seeded.
// Seeded rows sit under an aggregate type of their own, so a reader of the table can tell them apart.
//
// The rows go straight into the table rather than through the actions that write them. Signing in with a wrong
// password a dozen times would test the sign-in handler, which has tests of its own, and would lock the account
// half way through; what this section has to get right is which stored rows it counts.
//
// Rows meant to be the newest are dated from the moment of seeding, after every account the test needs has been
// created, and the seeding then waits a quarter of a second, so the dashboard's moment of asking falls after them.
//
//
// WHERE THE SEEDED ROWS ARE PUT IN TIME, AND WHY
//
// The day the counts start from is the oldest stored sign-in row, read over the whole table, and the spike rule
// trusts the days after it. A sign-in row seeded three days back would tell every later dashboard in this run that
// three days had been counted, and the dozens of wrong passwords other classes type today would then read as a
// spike on every dashboard after this one. So sign-in rows are only ever seeded inside the last hour through the
// route, which moves that day no earlier than the run itself began, and the windows' edges are tested through the
// route on the three events that are not sign-in rows.
//
// The spike rule needs history, and history cannot be written in the past without that effect. So the spike and
// every edge of every window are tested on the section itself, resolved from a scope like the dashboard resolves
// it and asked as of a moment far in the future, with rows seeded around that moment. Every reader of the table
// asks up to now, so rows dated in 2091 are invisible to every dashboard, overview and search in this run, and the
// oldest stored sign-in row is still today's, so the week before that moment counts as fully stored.

namespace MotsSupplierPortal.Tests.Integration.Admin.Dashboard;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class SecuritySectionTests(PostgresApiFixture fixture)
{
    private const string Route = "/api/v1/admin/dashboard";

    private const string ProbeAggregate = "DashboardSecurityProbe";

    private static readonly string[] SevenEvents =
    [
        "login_failed", "login_locked_out", "login_mfa_failed", "login_blocked_mfa_enrollment_required",
        "refresh_reuse_detected", "password_reset", "staff_mfa_reset",
    ];

    private static readonly string[] NotSignInRows = ["refresh_reuse_detected", "password_reset", "staff_mfa_reset"];

    [Fact]
    public async Task Each_event_is_counted_over_the_last_24_hours_and_the_last_7_days_always_all_seven_in_order()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var before = await SecurityAsync(admin);

        var now = DateTimeOffset.UtcNow;
        var rows = new List<AuditLog>();
        foreach (var action in SevenEvents)
        {
            rows.Add(Row(action, now.AddHours(-1)));
        }

        foreach (var action in NotSignInRows)
        {
            rows.Add(Row(action, now.AddDays(-3)));
            rows.Add(Row(action, now.AddDays(-8)));
            rows.Add(Row(action, now.AddHours(1)));
        }

        rows.Add(Row("login_succeeded", now.AddHours(-1)));
        rows.Add(Row("password_changed", now.AddHours(-1)));
        await SeedAsync(rows);

        var after = await SecurityAsync(admin);

        Actions(after).Should().Equal(SevenEvents, "every event is always there, a zero included, in one order");

        foreach (var action in SevenEvents)
        {
            var notSignIn = NotSignInRows.Contains(action);
            (Count(after, action, "last24Hours") - Count(before, action, "last24Hours")).Should().Be(1,
                $"{action}: the row an hour ago is in the last 24 hours, and the one an hour ahead is not yet");
            (Count(after, action, "last7Days") - Count(before, action, "last7Days")).Should().Be(notSignIn ? 2 : 1,
                $"{action}: the rows an hour and three days ago are in the week, the eight-day-old one is not");
        }
    }

    [Fact]
    public async Task The_counts_say_from_when_they_were_counted_and_nothing_spikes_on_the_first_day()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var oldest = DateTimeOffset.UtcNow.AddMinutes(-55);
        await SeedAsync([Row("login_failed", oldest)]);

        var security = await SecurityAsync(admin);

        var countedSince = security.GetProperty("countedSince").GetDateTimeOffset();
        countedSince.Should().BeOnOrBefore(oldest.AddMilliseconds(1),
            "the counts start from the oldest stored sign-in row, and one was seeded at this moment");
        countedSince.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(-1),
            "this run stores no sign-in row older than itself, so the counts start inside today");

        security.GetProperty("events").EnumerateArray()
            .Should().OnlyContain(e => !e.GetProperty("spiking").GetBoolean(),
                "with less than a day stored before the last 24 hours there is nothing to call a spike against");
    }

    [Fact]
    public async Task Windows_spikes_and_the_moment_of_asking_are_measured_from_the_dashboards_instant()
    {
        var asOf = new DateTimeOffset(2091, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var rows = new List<AuditLog>();

        // login_failed: six in the last hour, and the edges. Exactly 24 hours back is in the day, a minute more is in
        // the week only; exactly 7 days back is in the week, a minute more is in neither; a minute after the moment of
        // asking is in neither. That is seven in the day against two over the six days before, so a spike.
        rows.AddRange(Enumerable.Range(0, 6).Select(i => Row("login_failed", asOf.AddMinutes(-10 - i))));
        rows.Add(Row("login_failed", asOf.AddHours(-24)));
        rows.Add(Row("login_failed", asOf.AddHours(-24).AddMinutes(-1)));
        rows.Add(Row("login_failed", asOf.AddDays(-7)));
        rows.Add(Row("login_failed", asOf.AddDays(-7).AddMinutes(-1)));
        rows.Add(Row("login_failed", asOf.AddMinutes(1)));

        // password_reset: six in the last day against thirty over the six days before, five a day, so not a spike.
        rows.AddRange(Enumerable.Range(0, 6).Select(i => Row("password_reset", asOf.AddHours(-2 - i))));
        rows.AddRange(Enumerable.Range(0, 30).Select(i => Row("password_reset", asOf.AddDays(-2).AddMinutes(-i))));

        // refresh_reuse_detected: four in the day is under the floor, however quiet the week.
        rows.AddRange(Enumerable.Range(0, 4).Select(i => Row("refresh_reuse_detected", asOf.AddHours(-3 - i))));

        await SeedAsync(rows);

        var security = await RunSectionAsync(asOf);

        security.Events.Select(e => e.Action).Should().Equal(SevenEvents);

        var failed = security.Events.Single(e => e.Action == "login_failed");
        failed.Last24Hours.Should().Be(7);
        failed.Last7Days.Should().Be(9);
        failed.Spiking.Should().BeTrue("seven against nothing on the six days before is a spike");

        var resets = security.Events.Single(e => e.Action == "password_reset");
        resets.Last24Hours.Should().Be(6);
        resets.Last7Days.Should().Be(36);
        resets.Spiking.Should().BeFalse("six is not three times five a day");

        var reuse = security.Events.Single(e => e.Action == "refresh_reuse_detected");
        (reuse.Last24Hours, reuse.Last7Days, reuse.Spiking).Should().Be((4, 4, false));

        security.Events.Where(e => e.Action is "login_locked_out" or "login_mfa_failed" or "staff_mfa_reset")
            .Should().OnlyContain(e => e.Last24Hours == 0 && e.Last7Days == 0 && !e.Spiking);

        security.CountedSince.Should().NotBeNull().And.BeBefore(asOf.AddDays(-7),
            "today's sign-in rows are the oldest, so the whole week before 2091 counts as stored");
    }

    [Fact]
    public async Task Sensitive_changes_are_the_ten_latest_on_the_allow_list_and_a_manual_import_but_not_a_scheduled_one()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (_, actorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);

        var allowed = DashboardAuditActions.SensitiveChanges
            .Select(action => (Action: action, Kind: AuditActorKind.User))
            .Append((Action: SupplierAuditActions.ErpImportRun, Kind: AuditActorKind.User))
            .ToArray();
        allowed.Should().HaveCount(14);
        allowed.Select(a => a.Action).Should().Contain(
            ["IntegrationConnectionUpdated", "IntegrationSupplierCreationChanged", "supplier.erp_push_retried", "ErpImportRun"]);

        // Two rounds of seven, so each round fits in the ten with room to spare. Each allowed row is written a
        // millisecond apart, and the rows that must be left out are written after them, so they are the newest and
        // would push an allowed row out of the ten if they were let in.
        foreach (var round in allowed.Chunk(7))
        {
            var start = DateTimeOffset.UtcNow;
            var shown = round
                .Select((a, i) => Row(a.Action, start.AddMilliseconds(i), a.Kind, actorId))
                .ToArray();
            var leftOut = new[]
            {
                Row(SupplierAuditActions.ErpImportRun, start.AddMilliseconds(100), AuditActorKind.System, label: "system"),
                Row("IntegrationConnectionTested", start.AddMilliseconds(101), AuditActorKind.User, actorId),
                Row("rfq_created", start.AddMilliseconds(102), AuditActorKind.User, actorId),
                Row("login_failed", start.AddMilliseconds(103), AuditActorKind.User, actorId),
            };
            await SeedAsync([.. shown, .. leftOut]);

            var changes = (await SecurityAsync(admin)).GetProperty("sensitiveChanges").EnumerateArray().ToArray();

            changes.Length.Should().BeLessThanOrEqualTo(10);
            changes.Take(shown.Length).Select(c => c.GetProperty("id").GetGuid())
                .Should().Equal(Enumerable.Reverse(shown).Select(r => r.Id), "the newest allowed rows lead, newest first");
            changes.Select(c => c.GetProperty("id").GetGuid())
                .Should().NotIntersectWith(leftOut.Select(r => r.Id));
        }
    }

    [Fact]
    public async Task Each_change_names_its_actor_by_current_name_then_by_label_then_as_the_system()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (_, actorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer);
        var currentName = $"Dashboard Actor {Guid.NewGuid():N}";

        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == actorId);
            user.FullName = currentName;
            await db.SaveChangesAsync();
        }

        var start = DateTimeOffset.UtcNow;
        var named = Row("setting.updated", start, AuditActorKind.User, actorId, label: "the name it was written with");
        var labelled = Row("api_key_revoked", start.AddMilliseconds(1), AuditActorKind.User, Guid.NewGuid(), "Departed Admin");
        var integration = Row("IntegrationConnectionUpdated", start.AddMilliseconds(2), AuditActorKind.Integration, label: "Ministry feed key");
        var nobody = Row("staff_deactivated", start.AddMilliseconds(3), AuditActorKind.System);
        var unknownAccount = Row("staff_reactivated", start.AddMilliseconds(4), AuditActorKind.User, Guid.NewGuid());
        await SeedAsync([named, labelled, integration, nobody, unknownAccount]);

        var changes = (await SecurityAsync(admin)).GetProperty("sensitiveChanges").EnumerateArray()
            .ToDictionary(c => c.GetProperty("id").GetGuid());

        Actor(changes[named.Id]).Should().Be(currentName, "an account that exists is named as it is called now");
        Actor(changes[labelled.Id]).Should().Be("Departed Admin", "an account that is gone falls back to the label");
        Actor(changes[integration.Id]).Should().Be("Ministry feed key");
        Actor(changes[nobody.Id]).Should().Be("system");
        Actor(changes[unknownAccount.Id]).Should().Be("system");

        var row = changes[named.Id];
        row.GetProperty("action").GetString().Should().Be("setting.updated");
        row.GetProperty("aggregateType").GetString().Should().Be(ProbeAggregate);
        row.GetProperty("occurredAt").GetDateTimeOffset().Should().BeCloseTo(start, TimeSpan.FromMilliseconds(1));
        changes[integration.Id].GetProperty("actorKind").GetString().Should().Be("Integration");
        row.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["id", "occurredAt", "action", "actorKind", "actorName", "aggregateType", "referenceCode"],
            "a row carries no reason, no changes and no states, so no award amount can reach this screen");
    }

    [Fact]
    public async Task A_viewer_without_audit_read_gets_the_section_hidden_with_no_data()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var original = await PermissionsOfAsync(admin, Roles.MinistryViewer);

        try
        {
            await SetPermissionsAsync(admin, Roles.MinistryViewer,
                [.. original.Where(p => p != Permissions.AuditRead).Append(Permissions.AdminUsersManage).Distinct()]);
            var viewer = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);

            var body = await DashboardAsync(viewer);

            foreach (var section in new[] { "security", "recentActivity" })
            {
                body.GetProperty(section).GetProperty("status").GetString().Should().Be("hidden", section);
                body.GetProperty(section).GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null, section);
            }

            body.GetRawText().Should().NotContain("sensitiveChanges").And.NotContain("login_failed");
        }
        finally
        {
            await SetPermissionsAsync(admin, Roles.MinistryViewer, original);
        }
    }

    private static AuditLog Row(
        string action, DateTimeOffset at, AuditActorKind kind = AuditActorKind.User, Guid? actor = null, string? label = null) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = at,
            ActorKind = kind,
            ActorUserId = actor,
            ActorLabel = label,
            AggregateType = ProbeAggregate,
            AggregateId = Guid.CreateVersion7(),
            Action = action,
            CorrelationId = Guid.CreateVersion7(),
        };

    private async Task SeedAsync(IEnumerable<AuditLog> rows)
    {
        await using var setup = fixture.Services.CreateAsyncScope();
        var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditLogs.AddRange(rows);
        await db.SaveChangesAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(250));
    }

    private async Task<DashboardSecurityDto> RunSectionAsync(DateTimeOffset asOf)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var section = scope.ServiceProvider.GetRequiredService<IDashboardSectionHandler<DashboardSecurityDto>>();
        var viewer = new DashboardViewer(null, new HashSet<string>([Permissions.AuditRead], StringComparer.Ordinal));
        return await section.RunAsync(new DashboardRequest(viewer, asOf), CancellationToken.None);
    }

    private static async Task<JsonElement> DashboardAsync(HttpClient client)
    {
        var response = await client.GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> SecurityAsync(HttpClient client)
    {
        var section = (await DashboardAsync(client)).GetProperty("security");
        section.GetProperty("status").GetString().Should().Be("ok");
        return section.GetProperty("data");
    }

    private static string[] Actions(JsonElement security) =>
        security.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("action").GetString()!).ToArray();

    private static int Count(JsonElement security, string action, string window) =>
        security.GetProperty("events").EnumerateArray()
            .Single(e => e.GetProperty("action").GetString() == action)
            .GetProperty(window).GetInt32();

    private static string Actor(JsonElement row) => row.GetProperty("actorName").GetString()!;

    private static async Task<string[]> PermissionsOfAsync(HttpClient admin, string role)
    {
        var roles = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/roles");
        return roles.GetProperty("roles").EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == role)
            .GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()!)
            .ToArray();
    }

    private static async Task SetPermissionsAsync(HttpClient admin, string role, string[] permissions)
    {
        var response = await admin.PutAsJsonAsync($"/api/v1/admin/roles/{role}/permissions", new { permissions });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
