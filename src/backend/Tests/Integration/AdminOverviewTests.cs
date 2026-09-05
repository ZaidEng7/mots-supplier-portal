using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-062/FR-DSH-006/SCR-700. <c>system_admin</c> had no landing page: the persona could authenticate
/// and had nowhere to go.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AdminOverviewTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminAsync() =>
        // system_admin needs MFA to obtain a session.
        StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    [Fact]
    public async Task The_dashboard_reports_users_reference_health_and_the_outbox()
    {
        var admin = await AdminAsync();

        var response = await admin.GetAsync("/api/v1/admin/overview");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Users by role - the admin themselves is at least one.
        body.GetProperty("usersByRole").EnumerateArray().Should().NotBeEmpty();
        body.GetProperty("totalRoles").GetInt32().Should().BeGreaterThan(0);

        // Reference-data health: all five tables, each with an active count. A table at zero active
        // codes blocks registration, and that is the fault this tile exists to surface.
        var tables = body.GetProperty("referenceData").EnumerateArray().ToList();
        tables.Should().HaveCount(5);
        tables.Select(t => t.GetProperty("table").GetString())
            .Should().BeEquivalentTo(ReferenceTables.All);
        tables.Should().OnlyContain(t => t.GetProperty("active").GetInt32() > 0,
            "every seeded reference table has at least one active code");

        // Outbox, and it must distinguish "nothing queued" from "queued and stuck".
        var outbox = body.GetProperty("outbox");
        outbox.GetProperty("pending").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        outbox.GetProperty("failed").GetInt32().Should().BeGreaterThanOrEqualTo(0);

        body.GetProperty("auditRowsLast24Hours").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task The_oldest_pending_outbox_age_distinguishes_a_fresh_queue_from_a_stuck_one()
    {
        var admin = await AdminAsync();

        // Age one pending message. A backlog COUNT alone cannot tell a normal queue from a dispatcher
        // that has stopped, which is the whole reason this field exists.
        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                Type = "AdminOverviewProbe",
                PayloadJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-3),
                SyncStatus = OutboxSyncStatus.Pending,
            });
            await db.SaveChangesAsync();
        }

        var body = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/overview");

        body.GetProperty("outbox").GetProperty("oldestPendingAgeMinutes").GetInt32()
            .Should().BeGreaterThanOrEqualTo(179, "a three-hour-old pending message is a stopped dispatcher");
    }

    [Fact]
    public async Task The_audit_count_is_a_24_hour_window_not_a_table_total()
    {
        var admin = await AdminAsync();

        var before = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/overview"))
            .GetProperty("auditRowsLast24Hours").GetInt32();

        // One row inside the window and one outside it. A tile reading the whole table would move by
        // two, and would then never fall - which is the failure mode that makes it useless as a sign
        // of activity.
        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogs.AddRange(
                NewAuditLog(DateTimeOffset.UtcNow.AddMinutes(-5)),
                NewAuditLog(DateTimeOffset.UtcNow.AddHours(-25)));
            await db.SaveChangesAsync();
        }

        var after = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/overview"))
            .GetProperty("auditRowsLast24Hours").GetInt32();

        after.Should().Be(before + 1, "the 25-hour-old row is outside the window");
    }

    private static AuditLog NewAuditLog(DateTimeOffset occurredAt) => new()
    {
        Id = Guid.CreateVersion7(),
        OccurredAt = occurredAt,
        ActorKind = AuditActorKind.System,
        AggregateType = "AdminOverviewProbe",
        AggregateId = Guid.CreateVersion7(),
        Action = "probe.written",
        CorrelationId = Guid.CreateVersion7(),
    };

    [Fact]
    public async Task Job_health_names_what_is_expected_and_what_is_actually_registered()
    {
        var admin = await AdminAsync();

        var jobs = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/overview")).GetProperty("jobs");

        // The expected list is the application's own, so this cannot drift from what Program.cs
        // registers.
        jobs.GetProperty("expectedJobs").EnumerateArray().Select(j => j.GetString())
            .Should().BeEquivalentTo(RecurringJobs.All);

        // The integration host runs with Jobs:EnableRecurring off, so NOTHING is registered and every
        // expected job is missing. That is the case the tile exists for - it is exactly what a
        // production host with the flag left off would look like, and today it is visible only as one
        // startup log line nobody reads on a running system.
        jobs.GetProperty("recurringJobsEnabled").GetBoolean().Should().BeFalse(
            "the test host disables recurring jobs, which is what makes the missing-jobs case observable here");
        jobs.GetProperty("missingJobs").EnumerateArray().Select(j => j.GetString())
            .Should().BeEquivalentTo(RecurringJobs.All);
    }

    [Fact]
    public async Task Nobody_without_admin_permission_can_read_it()
    {
        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.ProcurementManager, Roles.MinistryViewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.GetAsync("/api/v1/admin/overview")).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{role} does not hold admin.users.manage");
        }

        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Admin Outsider Co");
        (await supplier.GetAsync("/api/v1/admin/overview")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // The control.
        var admin = await AdminAsync();
        (await admin.GetAsync("/api/v1/admin/overview")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
