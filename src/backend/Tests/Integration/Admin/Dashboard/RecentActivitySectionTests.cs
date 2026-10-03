// The recent activity section of the administrator's dashboard: how many rows people and other systems wrote in the
// last 24 hours, the ten newest of them with the name of whoever wrote each, and the system's own rows counted apart
// and kept out of both. Through the route, except for the quiet day, which says above itself why it cannot be.
//
// The audit table is shared by every class in the run and nothing can be deleted from it, so the counts are asserted
// as the change between two readings around the rows a test seeds, and the list on the ids of those rows. The rows go
// straight into the table, under an aggregate type of their own, because what this section has to get right is which
// stored rows it shows, not how the actions that write them work.
//
// The seeded rows are dated from the moment of seeding, after every account the test needs has been created and
// signed in, so they are the newest rows of their kind and the feed's first rows are theirs to assert. The seeding
// then waits a quarter of a second, so the dashboard's moment of asking falls after every row it dated. The test
// host runs no recurring jobs, which is what keeps the system's count still between the two readings.
//
// Every action SessionAuditActions names is seeded once under a person and once under the system, beside the few
// written out by hand, so a count that took in any session row, or left them out by a shorter list of its own,
// moves by more than it should. The overview's test held the same line before the overview was retired.

namespace MotsSupplierPortal.Tests.Integration.Admin.Dashboard;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class RecentActivitySectionTests(PostgresApiFixture fixture)
{
    private const string Route = "/api/v1/admin/dashboard";

    private const string ProbeAggregate = "DashboardActivityProbe";

    [Fact]
    public async Task People_and_integrations_are_counted_and_listed_and_the_system_is_counted_apart()
    {
        var (admin, adminId) = await StaffTestClient.CreateWithMfaAndIdAsync(fixture, Roles.SystemAdmin);
        var before = await ActivityAsync(admin);

        var start = DateTimeOffset.UtcNow;
        var person = Row("rfq_created", start, AuditActorKind.User, adminId);
        var integration = Row("MinistrySupplierFeedExported", start.AddMilliseconds(1), AuditActorKind.Integration, label: "Ministry feed key");
        var manualImport = Row(SupplierAuditActions.ErpImportRun, start.AddMilliseconds(2), AuditActorKind.User, adminId);
        var manualImportDone = Row(SupplierAuditActions.ErpImportCompleted, start.AddMilliseconds(3), AuditActorKind.User, adminId);
        var shown = new[] { person, integration, manualImport, manualImportDone };

        var systemRows = new[]
        {
            Row("document_expired", start.AddMilliseconds(10), AuditActorKind.System),
            Row(SupplierAuditActions.ErpImportRun, start.AddMilliseconds(11), AuditActorKind.System, label: "system"),
            Row(SupplierAuditActions.ErpImportCompleted, start.AddMilliseconds(12), AuditActorKind.System, label: "system"),
            Row(SupplierAuditActions.ErpImportFailed, start.AddMilliseconds(13), AuditActorKind.System, label: "system"),
            Row(SupplierAuditActions.ErpPushAttemptFailed, start.AddMilliseconds(14), AuditActorKind.System),
        };

        var neither = new[]
        {
            Row(SupplierAuditActions.ErpPushAttemptFailed, start.AddMilliseconds(20), AuditActorKind.User, adminId),
            Row("login_succeeded", start.AddMilliseconds(21), AuditActorKind.User, adminId),
            Row("logout", start.AddMilliseconds(22), AuditActorKind.User, adminId),
            Row("login_failed", start.AddMilliseconds(23), AuditActorKind.System),
        }.Concat(SessionAuditActions.All.SelectMany((action, i) => new[]
        {
            Row(action, start.AddMilliseconds(30 + (2 * i)), AuditActorKind.User, adminId),
            Row(action, start.AddMilliseconds(31 + (2 * i)), AuditActorKind.System),
        })).ToArray();

        var yesterday = Row("rfq_updated", DateTimeOffset.UtcNow.AddHours(-25), AuditActorKind.User, adminId);
        var ahead = Row("rfq_updated", DateTimeOffset.UtcNow.AddHours(1), AuditActorKind.User, adminId);

        await SeedAsync([.. shown, .. systemRows, .. neither, yesterday, ahead]);

        var after = await ActivityAsync(admin);

        (after.GetProperty("last24Hours").GetInt32() - before.GetProperty("last24Hours").GetInt32()).Should().Be(4,
            "a person's rows and an integration's count, the manual import's two included; the system's rows, the "
            + "sessions, a push attempt, yesterday and a row dated ahead of the moment of asking do not");
        (after.GetProperty("systemLast24Hours").GetInt32() - before.GetProperty("systemLast24Hours").GetInt32()).Should().Be(5,
            "the system's own rows, the hourly sync's bookkeeping among them, are counted apart; a session row is not");

        var latest = after.GetProperty("latest").EnumerateArray().ToArray();
        latest.Length.Should().BeLessThanOrEqualTo(10);
        latest.Take(shown.Length).Select(r => r.GetProperty("id").GetGuid())
            .Should().Equal(Enumerable.Reverse(shown).Select(r => r.Id), "the newest of the feed's own rows, newest first");
        latest.Select(r => r.GetProperty("id").GetGuid())
            .Should().NotIntersectWith(systemRows.Concat(neither).Append(ahead).Select(r => r.Id));

        var byId = latest.ToDictionary(r => r.GetProperty("id").GetGuid());
        byId[person.Id].GetProperty("actorName").GetString().Should().Be("Integration Staff (MFA)");
        byId[person.Id].GetProperty("actorKind").GetString().Should().Be("User");
        byId[integration.Id].GetProperty("actorName").GetString().Should().Be("Ministry feed key");
        byId[integration.Id].GetProperty("actorKind").GetString().Should().Be("Integration");
        byId[manualImport.Id].GetProperty("action").GetString().Should().Be("ErpImportRun");
    }

    // A quiet day cannot be made through the route, because every class in the run writes rows today. So the section
    // is resolved from a scope like the dashboard resolves it and asked as of a moment in 2092, three days after the
    // twelve rows this test seeds: nothing in that moment's last 24 hours, and these rows the newest before it. Every
    // other reader of the table asks up to now, so the rows are invisible to them.
    [Fact]
    public async Task After_a_quiet_day_the_feed_still_shows_the_ten_last_things_that_happened()
    {
        var asOf = new DateTimeOffset(2092, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var rows = Enumerable.Range(0, 12)
            .Select(i => Row("rfq_cancelled", asOf.AddDays(-3).AddMinutes(i), AuditActorKind.User))
            .ToArray();
        await SeedAsync(rows);

        await using var scope = fixture.Services.CreateAsyncScope();
        var section = scope.ServiceProvider.GetRequiredService<IDashboardSectionHandler<DashboardRecentActivityDto>>();
        var viewer = new DashboardViewer(null, new HashSet<string>([Permissions.AuditRead], StringComparer.Ordinal));
        var activity = await section.RunAsync(new DashboardRequest(viewer, asOf), CancellationToken.None);

        activity.Last24Hours.Should().Be(0);
        activity.SystemLast24Hours.Should().Be(0);
        activity.Latest.Select(r => r.Id).Should().Equal(Enumerable.Reverse(rows).Take(10).Select(r => r.Id),
            "the list is the ten newest, newest first, whatever their age, and not the last day's");
        activity.Latest[0].ActorName.Should().Be("system", "a row with no account and no label is the product's own");
    }

    private static AuditLog Row(
        string action, DateTimeOffset at, AuditActorKind kind, Guid? actor = null, string? label = null) =>
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

    private static async Task<JsonElement> ActivityAsync(HttpClient client)
    {
        var response = await client.GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var section = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recentActivity");
        section.GetProperty("status").GetString().Should().Be("ok");
        return section.GetProperty("data");
    }
}
