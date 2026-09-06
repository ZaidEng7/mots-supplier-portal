using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// SCR-721 and SCR-722. T-083 left both as counters; these are the per-row views and the two actions.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class OperationsEndpointsTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    private async Task<Guid> SeedOutboxAsync(OutboxSyncStatus status)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = $"test.event.{Guid.NewGuid():N}"[..24],
            PayloadJson = """{"probe":true}""",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            SyncStatus = status,
            ProcessedAt = status == OutboxSyncStatus.Pending ? null : DateTimeOffset.UtcNow.AddMinutes(-5),
        };
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    [Fact]
    public async Task Every_expected_job_gets_a_row_whether_or_not_Hangfire_holds_it()
    {
        var admin = await AdminAsync();

        var monitor = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/jobs");
        var ids = monitor.GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("id").GetString()).ToList();

        // Driven by the expected list, not by what Hangfire happens to hold: a monitor that listed only
        // what exists could never show the one thing worth showing, which is a job that should be there
        // and is not. The test fixture registers no recurring jobs, so this asserts exactly that case.
        ids.Should().Contain(RecurringJobs.All);

        // The fault is per row, not just a count: every row says whether Hangfire holds that job.
        var rows = monitor.GetProperty("jobs").EnumerateArray().ToList();
        rows.Should().NotBeEmpty();
        rows.Select(row => row.GetProperty("registered").GetBoolean()).Should().AllSatisfy(
            registered => registered.Should().BeFalse("this fixture registers no recurring jobs"));
    }

    [Fact]
    public async Task Triggering_a_job_Hangfire_does_not_hold_is_a_404_not_a_cheerful_202()
    {
        var admin = await AdminAsync();

        // An operator pressing Run now on a job a deployment dropped has to learn that. Reporting
        // success for nothing happening is the failure mode this asserts against.
        (await admin.PostAsync("/api/v1/admin/jobs/not-a-real-job/trigger", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And an expected-but-unregistered job answers the same way, because the question the endpoint
        // asks is "can Hangfire run this", not "did we mean to register it".
        (await admin.PostAsync($"/api/v1/admin/jobs/{RecurringJobs.All[0]}/trigger", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "this fixture registers no recurring jobs, so nothing here is runnable");
    }

    [Fact]
    public async Task The_outbox_reports_every_status_including_the_empty_ones()
    {
        await SeedOutboxAsync(OutboxSyncStatus.Failed);
        var admin = await AdminAsync();

        var monitor = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/outbox");
        var counts = monitor.GetProperty("counts");

        // An operator who cannot see "Failed: 0" cannot tell it from a count that failed to load, so
        // every status is present whether or not it has rows.
        foreach (var status in Enum.GetNames<OutboxSyncStatus>())
        {
            counts.TryGetProperty(status, out _).Should().BeTrue($"{status} must be reported even at zero");
        }

        counts.GetProperty("Failed").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task An_unrecognised_status_is_refused_rather_than_returning_everything()
    {
        await SeedOutboxAsync(OutboxSyncStatus.Sent);
        var admin = await AdminAsync();

        // The failure this closes: the first version parsed with Enum.TryParse and ignored failure, so a
        // typo applied NO predicate and returned every row - the caller asked to narrow and got the
        // opposite with no way to tell. Caught by FilterGuardTests, not by review.
        var refused = await admin.GetAsync("/api/v1/admin/outbox?status=Faild");
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("type").GetString().Should().EndWith("/errors/validation");

        // Both controls: a recognised value is accepted, and §6.2's multi-value OR form works - which is
        // the question an operator actually asks, "show me everything not yet delivered".
        (await admin.GetAsync("/api/v1/admin/outbox?status=Sent")).StatusCode.Should().Be(HttpStatusCode.OK);

        var both = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/outbox?status=Failed,Pending");
        both.GetProperty("messages").EnumerateArray()
            .Select(m => m.GetProperty("syncStatus").GetString())
            .Should().OnlyContain(state => state == "Failed" || state == "Pending",
                "a Sent message must not come back from a Failed,Pending filter");
    }

    [Fact]
    public async Task Only_a_failed_message_replays()
    {
        var failed = await SeedOutboxAsync(OutboxSyncStatus.Failed);
        var sent = await SeedOutboxAsync(OutboxSyncStatus.Sent);
        var pending = await SeedOutboxAsync(OutboxSyncStatus.Pending);
        var admin = await AdminAsync();

        (await admin.PostAsync($"/api/v1/admin/outbox/{failed}/replay", null))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        // The guard both ways, and this is the half that matters: the outbox exists to make delivery
        // exactly-once, and an admin button that re-sends a delivered integration event breaks the
        // guarantee the whole pattern is for.
        (await admin.PostAsync($"/api/v1/admin/outbox/{sent}/replay", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "re-sending a Sent message would deliver twice");
        (await admin.PostAsync($"/api/v1/admin/outbox/{pending}/replay", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "a Pending message is already going to be attempted");
        (await admin.PostAsync($"/api/v1/admin/outbox/{Guid.CreateVersion7()}/replay", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Asserted on the row, not on the response: replay means Pending again with no processed time,
        // so the dispatcher picks it up on its next pass.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var replayed = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == failed);
        replayed.SyncStatus.Should().Be(OutboxSyncStatus.Pending);
        replayed.ProcessedAt.Should().BeNull();

        var untouched = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == sent);
        untouched.SyncStatus.Should().Be(OutboxSyncStatus.Sent);
    }

    [Fact]
    public async Task Nobody_without_admin_permission_reaches_any_of_it()
    {
        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.ProcurementManager, Roles.MinistryViewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.GetAsync("/api/v1/admin/jobs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await staff.GetAsync("/api/v1/admin/outbox")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await staff.PostAsync("/api/v1/admin/jobs/outbox-dispatch/trigger", null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} must not be able to run platform jobs");
        }

        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Ops Outsider Co");
        (await supplier.GetAsync("/api/v1/admin/outbox")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the payloads on this screen are integration data");

        // The control, so every refusal above is the permission and not a route that refuses everyone.
        var admin = await AdminAsync();
        (await admin.GetAsync("/api/v1/admin/jobs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/v1/admin/outbox")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
