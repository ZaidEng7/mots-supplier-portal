// The system health section of the administrator's dashboard, read through the dashboard's own route: every
// figure is seeded, counted and asserted from the answer a browser gets.
//
// The frame around the section, its gate and what a failed section looks like, is AdminDashboardFrameTests'. This
// file is about what the section says.
//
//
// THE JOB VERDICTS ARE DRIVEN THROUGH A STAND-IN JOBS MONITOR
//
// The suite runs with recurring jobs switched off, and the scheduler's recurring-job rows are shared by every
// class, so the eight jobs cannot be made late, failed or missing for real without disturbing the rest of the run.
// The verdicts are therefore read from a host whose jobs monitor answers rows the test writes, measured from the
// moment the section asks. The monitor itself is OperationsEndpointsTests' to test. Each threshold is asserted a
// couple of minutes either side of its line, so a threshold that moved by more than that fails here.
//
// One test reads the real monitor on the fixture's own host, where schedules are off: all eight rows, in the
// order RecurringJobs.All gives, each disabled, with its own threshold and link.
//
//
// THE QUEUE AND MAIL ARE COUNTED IN A SCHEDULER STORAGE OF THEIR OWN
//
// The fixture's own host runs jobs from the shared scheduler storage while these tests run, so a count over it
// moves under the test's feet. The queue test builds a scheduler storage in a schema of its own in the same
// database, hands it to a derived host as the job storage and names it as that host's Hangfire:SchemaName (the mail
// figures are counted in the scheduler's tables by that name), seeds exactly what it counts, and drops the schema
// at the end. A seeded failure's time is the time its Failed state row was written, which the test moves back. The derived host runs no jobs, as every derived host here does not, so nothing it seeds is ever taken.
//
//
// THE REST IS SEEDED IN THE SHARED DATABASE AND COUNTED AS A DIFFERENCE
//
// Outbox messages, documents and awards are seeded on the fixture's database, counted before and after, and
// removed at the end. Rows other classes leave behind move both readings equally. The reference lists and the
// migration history are changed for the length of one request and put back in a finally, because the fixture
// compares the global rows at the end of the run and every other class relies on a fully migrated database.
//
//
// THE OBJECT STORE IS ASKED THROUGH ITS READINESS CHECK
//
// The host cannot be started with the object store broken, because its own startup creates the bucket. The
// failing cases therefore replace the readiness check the section runs: one that reports unhealthy, one that
// never answers and ignores its cancellation, and none at all. The one that never answers proves the five-second
// cap by the time the whole dashboard takes to come back.

namespace MotsSupplierPortal.Tests.Integration.Admin.Dashboard;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Hangfire.Server;
using Hangfire.States;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Integration.Erp;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using Npgsql;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class SystemHealthSectionTests(PostgresApiFixture fixture)
{
    private const string Route = "/api/v1/admin/dashboard";

    private const string ObjectStorageCheck = "object-storage";

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static readonly string[] FiveMinuteJobs = ["outbox-dispatch", "rfq-timeline", "award-erp-sync", "supplier-erp-push"];

    private static readonly string[] HourlyJobs = ["idempotency-cleanup", "erp-supplier-sync"];

    private static readonly string[] DailyJobs = ["document-expiry-lifecycle", "draft-registration-cleanup"];

    [Fact]
    public async Task Every_one_of_the_eight_jobs_has_a_row_in_order_with_its_threshold_and_its_link()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var health = await SystemHealthAsync(admin);
        var jobs = health.GetProperty("jobs");

        jobs.GetProperty("recurringEnabled").GetBoolean().Should().BeFalse("the suite runs with schedules switched off");

        var rows = jobs.GetProperty("jobs").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("id").GetString()).Should().Equal(RecurringJobs.All);
        RecurringJobs.All.Should().BeEquivalentTo([.. FiveMinuteJobs, .. HourlyJobs, .. DailyJobs],
            "a ninth job needs a threshold of its own on the dashboard, and a line here");

        foreach (var row in rows)
        {
            var id = row.GetProperty("id").GetString()!;

            row.GetProperty("verdict").GetString().Should().Be("disabled",
                $"{id} cannot run on a timer while schedules are off, and that comes before every other verdict");

            row.GetProperty("lateAfterMinutes").GetInt32().Should().Be(
                FiveMinuteJobs.Contains(id) ? 15 : HourlyJobs.Contains(id) ? 180 : 26 * 60, id);

            row.GetProperty("link").GetString().Should().Be(
                id == "erp-supplier-sync" ? "/back-office/erp-import" : "/back-office/operations", id);
        }
    }

    [Fact]
    public async Task Each_job_is_judged_against_its_own_threshold_with_late_before_failed_before_retrying()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var monitor = new ScriptedJobsMonitor();
        await using var host = HostWith(services => services.AddScoped<IGetJobsMonitorHandler>(_ => monitor));

        monitor.Script = now =>
        [
            Ran("document-expiry-lifecycle", now - TimeSpan.FromHours(26) + 2 * Minute, "Succeeded"),
            Ran("draft-registration-cleanup", now - TimeSpan.FromHours(26) - 2 * Minute, "Succeeded"),
            Ran("outbox-dispatch", now - 13 * Minute, "Succeeded"),
            Ran("rfq-timeline", now - 17 * Minute, "Succeeded"),
            Ran("award-erp-sync", now - 2 * Minute, "Failed"),
            Ran("idempotency-cleanup", now - TimeSpan.FromHours(3) + 2 * Minute, "Scheduled"),
            Ran("erp-supplier-sync", now - TimeSpan.FromHours(3) - 2 * Minute, "Succeeded"),
            Ran("supplier-erp-push", now - 17 * Minute, "Failed"),
            Ran("an-orphan-from-an-old-deployment", now - TimeSpan.FromDays(30), "Failed"),
        ];

        var first = Verdicts(await SystemHealthAsync(host, admin));

        first.Should().Equal(new Dictionary<string, string>
        {
            ["document-expiry-lifecycle"] = "ok",
            ["draft-registration-cleanup"] = "late",
            ["outbox-dispatch"] = "ok",
            ["rfq-timeline"] = "late",
            ["award-erp-sync"] = "failed",
            ["idempotency-cleanup"] = "retrying",
            ["erp-supplier-sync"] = "late",
            ["supplier-erp-push"] = "late",
        }, "daily jobs are late after 26 hours, hourly ones after 3, five-minute ones after 15; Scheduled is a retry; "
           + "a late job is late whatever its last state; and a job this application does not schedule is not listed");

        monitor.Script = now =>
        [
            new RecurringJobRowDto("document-expiry-lifecycle", false, null, null, null, null),
            Ran("draft-registration-cleanup", now - TimeSpan.FromHours(4), "Succeeded"),
            NeverRan("outbox-dispatch", now - 8 * Minute),
            NeverRan("rfq-timeline", now - 12 * Minute),
            NeverRan("award-erp-sync", now + 3 * Minute),
            Ran("idempotency-cleanup", now - TimeSpan.FromHours(2), "Succeeded"),
            Ran("erp-supplier-sync", now - TimeSpan.FromHours(3) + 2 * Minute, "Processing"),
            Ran("supplier-erp-push", now - 13 * Minute, "Succeeded"),
        ];

        var second = Verdicts(await SystemHealthAsync(host, admin));

        second.Should().Equal(new Dictionary<string, string>
        {
            ["document-expiry-lifecycle"] = "missing",
            ["draft-registration-cleanup"] = "ok",
            ["outbox-dispatch"] = "ok",
            ["rfq-timeline"] = "late",
            ["award-erp-sync"] = "ok",
            ["idempotency-cleanup"] = "ok",
            ["erp-supplier-sync"] = "ok",
            ["supplier-erp-push"] = "ok",
        }, "an unregistered job is missing; a daily job 4 hours on is not late; and a job that has never run is late "
           + "once its first run is more than the grace overdue, 10 minutes for a five-minute job");

        monitor.Script = now => [Ran("outbox-dispatch", now - TimeSpan.FromDays(3), "Failed")];
        monitor.Enabled = false;

        var third = Verdicts(await SystemHealthAsync(host, admin));
        third.Values.Should().OnlyContain(v => v == "disabled",
            "with schedules switched off for the deployment no job runs on a timer, so none of them can be late");
    }

    [Fact]
    public async Task A_job_row_carries_the_scheduler_evidence_beside_its_verdict()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var last = DateTimeOffset.UtcNow.AddMinutes(-2);
        var next = last.AddMinutes(5);
        var monitor = new ScriptedJobsMonitor
        {
            Script = _ => [new RecurringJobRowDto("award-erp-sync", true, "*/5 * * * *", last, next, "Failed")],
        };
        await using var host = HostWith(services => services.AddScoped<IGetJobsMonitorHandler>(_ => monitor));

        var row = (await SystemHealthAsync(host, admin)).GetProperty("jobs").GetProperty("jobs").EnumerateArray()
            .Single(r => r.GetProperty("id").GetString() == "award-erp-sync");

        row.GetProperty("verdict").GetString().Should().Be("failed");
        row.GetProperty("lastState").GetString().Should().Be("Failed");
        row.GetProperty("lastExecution").GetDateTimeOffset().Should().BeCloseTo(last, TimeSpan.FromMilliseconds(1));
        row.GetProperty("nextExecution").GetDateTimeOffset().Should().BeCloseTo(next, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task The_queue_and_mail_are_the_schedulers_own_counts_less_the_imports_second_try()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var schema = $"dashboard_hangfire_{Guid.NewGuid():N}"[..40];
        var connectionString = fixture.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Default"]!;
        var options = new PostgreSqlStorageOptions { SchemaName = schema, PrepareSchemaIfNecessary = true };
        var storage = new PostgreSqlStorage(new NpgsqlConnectionFactory(connectionString, options), options);

        try
        {
            var client = new BackgroundJobClient(storage);
            var later = new ScheduledState(TimeSpan.FromDays(1));

            client.Create(Job.FromExpression<OutboxDispatcher>(j => j.DispatchPendingAsync(CancellationToken.None)), later);
            client.Create(Job.FromExpression<OutboxDispatcher>(j => j.DispatchPendingAsync(CancellationToken.None)), later);
            client.Create(Job.FromExpression<ErpSupplierSyncJob>(j => j.RunAgainAsync(CancellationToken.None)), later);
            client.Create(Job.FromExpression<EmailJobs>(j => j.SendVerificationEmailAsync(Guid.Empty, CancellationToken.None)), later);

            client.Create(Job.FromExpression<OutboxDispatcher>(j => j.DispatchPendingAsync(CancellationToken.None)), new EnqueuedState());

            var failures = new List<(string JobId, DateTime At)>();
            void FailAt(Job job, DateTime at) => failures.Add((client.Create(job, new FailedAt(at)), at));

            FailAt(Email(), DateTime.UtcNow.AddMinutes(-5));
            FailAt(Email(), DateTime.UtcNow.AddDays(-6));
            FailAt(Email(), DateTime.UtcNow.AddDays(-8));
            FailAt(Job.FromExpression<OutboxDispatcher>(j => j.DispatchPendingAsync(CancellationToken.None)),
                DateTime.UtcNow.AddMinutes(-5));

            using (var connection = storage.GetConnection())
            {
                var context = new ServerContext { Queues = ["default"], WorkerCount = 1 };
                connection.AnnounceServer("dashboard-live-server", context);
                connection.AnnounceServer("dashboard-silent-server", context);
            }

            await using (var sql = new NpgsqlConnection(connectionString))
            {
                await sql.OpenAsync();
                await using var silence = new NpgsqlCommand(
                    $"UPDATE \"{schema}\".server SET lastheartbeat = @at WHERE id = 'dashboard-silent-server'", sql);
                silence.Parameters.AddWithValue("at", DateTime.UtcNow.AddMinutes(-6));
                (await silence.ExecuteNonQueryAsync()).Should().Be(1);

                // The section reads when a job failed from the time its Failed state was written, so each seeded
                // failure's state row is moved back to the failure time it was given.
                foreach (var (jobId, at) in failures)
                {
                    await using var backdate = new NpgsqlCommand(
                        $"UPDATE \"{schema}\".state SET createdat = @at "
                        + $"WHERE id = (SELECT stateid FROM \"{schema}\".job WHERE id = @job)", sql);
                    backdate.Parameters.AddWithValue("at", new DateTimeOffset(at, TimeSpan.Zero));
                    backdate.Parameters.AddWithValue("job", long.Parse(jobId, System.Globalization.CultureInfo.InvariantCulture));
                    (await backdate.ExecuteNonQueryAsync()).Should().Be(1);
                }
            }

            await using var host = HostWith(
                services => services.AddSingleton<JobStorage>(storage),
                ("Hangfire:SchemaName", schema));
            var health = await SystemHealthAsync(host, admin);

            var queue = health.GetProperty("queue");
            queue.GetProperty("retrying").GetInt64().Should().Be(3,
                "four jobs wait in Scheduled, and one of them is the import's second try, which is not a retry");
            queue.GetProperty("enqueued").GetInt64().Should().Be(1);
            queue.GetProperty("processing").GetInt64().Should().Be(0);
            queue.GetProperty("failed").GetInt64().Should().Be(4);
            queue.GetProperty("liveServers").GetInt32().Should().Be(1,
                "a server silent for 6 minutes is gone, whatever row the scheduler still keeps for it");
            queue.GetProperty("heartbeatWithinMinutes").GetInt32().Should().Be(5);

            var email = health.GetProperty("email");
            email.GetProperty("failedInWindow").GetInt32().Should().Be(2,
                "two emails failed in the last 7 days; one failed 8 days ago, and the other failure is not an email");
            email.GetProperty("retrying").GetInt32().Should().Be(1);
            email.GetProperty("windowDays").GetInt32().Should().Be(7);
        }
        finally
        {
            await using var sql = new NpgsqlConnection(connectionString);
            await sql.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", sql);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Outbox_messages_pending_for_longer_than_15_minutes_are_stuck()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var now = DateTimeOffset.UtcNow;
        var seeded = new[]
        {
            Message(OutboxSyncStatus.Pending, now - TimeSpan.FromMinutes(20)),
            Message(OutboxSyncStatus.Pending, now - TimeSpan.FromMinutes(10)),
            Message(OutboxSyncStatus.Failed, now - TimeSpan.FromHours(2)),
            Message(OutboxSyncStatus.Sent, now - TimeSpan.FromHours(2)),
        };

        var before = (await SystemHealthAsync(admin)).GetProperty("outbox");

        try
        {
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.OutboxMessages.AddRange(seeded);
                await db.SaveChangesAsync();
            }

            var after = (await SystemHealthAsync(admin)).GetProperty("outbox");

            Delta(before, after, "pending").Should().Be(2);
            Delta(before, after, "stuck").Should().Be(1, "only the message pending for 20 minutes is past the line");
            Delta(before, after, "failed").Should().Be(1);
            after.GetProperty("stuckAfterMinutes").GetInt32().Should().Be(15);
            after.GetProperty("oldestPendingAt").GetDateTimeOffset().Should()
                .BeOnOrBefore(seeded[0].CreatedAt.AddMilliseconds(1));
        }
        finally
        {
            var ids = seeded.Select(m => m.Id).ToList();
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.OutboxMessages.Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Only_supplier_documents_waiting_for_the_scanner_15_minutes_after_upload_are_stuck_scans()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var name = $"Stuck Scan {Guid.NewGuid():N}"[..24];
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);
        var (_, attachmentId) = await TenderAttachmentAsync();

        var documents = new List<Guid>();

        try
        {
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var supplierId = await db.Suppliers.Where(s => s.DisplayNameEn == name).Select(s => s.Id).SingleAsync();
                var typeId = await db.DocumentTypes.Where(t => t.Code == "commercial_registration")
                    .Select(t => t.Id).SingleAsync();

                foreach (var _ in Enumerable.Range(0, 3))
                {
                    var document = SupplierDocument.CreatePendingScan(
                        $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}", supplierId, typeId, 1,
                        $"quarantine/dashboard/{Guid.NewGuid():N}.pdf", "licence.pdf", "application/pdf", 24,
                        Guid.CreateVersion7(), issueDate: null, expiryDate: null, expiryTracked: false,
                        today: DateOnly.FromDateTime(DateTime.UtcNow));
                    db.SupplierDocuments.Add(document);
                    documents.Add(document.Id);
                }

                await db.SaveChangesAsync();
            }

            var before = (await SystemHealthAsync(admin)).GetProperty("scans");

            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var longAgo = DateTimeOffset.UtcNow.AddMinutes(-20);

                await db.SupplierDocuments.Where(d => d.Id == documents[0])
                    .ExecuteUpdateAsync(set => set.SetProperty(d => d.UploadedAt, longAgo));
                await db.SupplierDocuments.Where(d => d.Id == documents[1])
                    .ExecuteUpdateAsync(set => set.SetProperty(d => d.UploadedAt, DateTimeOffset.UtcNow.AddMinutes(-10)));
                await db.SupplierDocuments.Where(d => d.Id == documents[2]).ExecuteUpdateAsync(set => set
                    .SetProperty(d => d.UploadedAt, longAgo)
                    .SetProperty(d => d.State, DocumentState.Uploaded));

                await db.Set<RfqAttachment>().Where(a => a.Id == attachmentId).ExecuteUpdateAsync(set => set
                    .SetProperty(a => a.UploadedAt, longAgo)
                    .SetProperty(a => a.ScanState, AttachmentScanState.PendingScan));
            }

            var after = (await SystemHealthAsync(admin)).GetProperty("scans");

            (after.GetProperty("stuck").GetInt32() - before.GetProperty("stuck").GetInt32()).Should().Be(1,
                "of the three documents only one is waiting for the scanner and older than 15 minutes, and the tender "
                + "file waiting just as long is scanned on another path and is not counted here");
            after.GetProperty("stuckAfterMinutes").GetInt32().Should().Be(15);
        }
        finally
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SupplierDocuments.Where(d => documents.Contains(d.Id)).ExecuteDeleteAsync();
            await db.Set<RfqAttachment>().Where(a => a.Id == attachmentId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Pending_migrations_are_named()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        (await SystemHealthAsync(admin)).GetProperty("pendingMigrations").EnumerateArray()
            .Should().BeEmpty("the fixture's database is fully migrated");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var latest = db.Database.GetMigrations().Last();
        var productVersion = await db.Database
            .SqlQuery<string>($"SELECT \"ProductVersion\" AS \"Value\" FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {latest}")
            .SingleAsync();

        await db.Database.ExecuteSqlAsync($"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {latest}");

        try
        {
            (await SystemHealthAsync(admin)).GetProperty("pendingMigrations").EnumerateArray()
                .Select(m => m.GetString()).Should().Equal([latest],
                    "a person applying migrations by hand needs the name of the one that is missing");
        }
        finally
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({latest}, {productVersion})");
        }
    }

    [Fact]
    public async Task Every_reference_list_is_reported_and_one_with_no_active_codes_says_so()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var active = await db.Set<Incoterm>().Where(i => i.IsActive).Select(i => i.Id).ToListAsync();
        var total = await db.Set<Incoterm>().CountAsync();
        active.Should().NotBeEmpty("the seeded incoterms are what this test switches off and back on");

        var lists = ReferenceLists(await SystemHealthAsync(admin));
        lists.Keys.Should().BeEquivalentTo(ReferenceTables.All);
        lists[ReferenceTables.Incoterms].Should().Be((active.Count, total - active.Count));
        lists[ReferenceTables.Currencies].Should().Be((
            await db.Set<Currency>().CountAsync(c => c.IsActive), await db.Set<Currency>().CountAsync(c => !c.IsActive)));

        await db.Set<Incoterm>().Where(i => active.Contains(i.Id)).ExecuteUpdateAsync(set => set.SetProperty(i => i.IsActive, false));

        try
        {
            ReferenceLists(await SystemHealthAsync(admin))[ReferenceTables.Incoterms].Should().Be((0, total),
                "a list with no active codes is what the screen has to be able to see");
        }
        finally
        {
            await db.Set<Incoterm>().Where(i => active.Contains(i.Id)).ExecuteUpdateAsync(set => set.SetProperty(i => i.IsActive, true));
        }
    }

    [Fact]
    public async Task The_purchase_order_transport_says_whether_it_sends_and_counts_the_failed_sends_without_an_amount()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var before = (await SystemHealthAsync(admin)).GetProperty("purchaseOrderTransport");
        before.GetProperty("configured").GetBoolean().Should().BeFalse("the suite runs on the logging stand-in");

        var failed = await FailedAwardSeed.CreateAsync(fixture, "Dashboard PO");

        try
        {
            var response = await admin.GetAsync(Route);
            var text = await response.Content.ReadAsStringAsync();
            var health = JsonDocument.Parse(text).RootElement.GetProperty("systemHealth").GetProperty("data");

            (health.GetProperty("purchaseOrderTransport").GetProperty("failedSends").GetInt32()
             - before.GetProperty("failedSends").GetInt32()).Should().Be(1);

            health.GetRawText().Should().NotContainEquivalentOf("amount", "the dashboard never shows an award amount");
            health.GetRawText().Should().NotContain(failed.RfqCode, "the section counts sends, it does not list them");

            await using var host = HostWith(services => services.AddSingleton<IOutboxTransport, SendingTransport>());
            (await SystemHealthAsync(host, admin)).GetProperty("purchaseOrderTransport").GetProperty("configured")
                .GetBoolean().Should().BeTrue("a real transport sends purchase orders");
        }
        finally
        {
            await FailedAwardSeed.RemoveAsync(fixture, failed.AwardId);
        }
    }

    [Fact]
    public async Task The_object_store_is_reachable_when_its_readiness_check_is_healthy()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        (await SystemHealthAsync(admin)).GetProperty("objectStorage").GetProperty("reachable").GetBoolean()
            .Should().BeTrue("the fixture's object store is up");

        var calls = new CallCounter();
        await using (var unhealthy = HostWith(services => ReplaceObjectStorageCheck(services, new FixedCheck(HealthStatus.Unhealthy, calls))))
        {
            (await SystemHealthAsync(unhealthy, admin)).GetProperty("objectStorage").GetProperty("reachable")
                .GetBoolean().Should().BeFalse();
            calls.Count.Should().Be(1, "the section asks the readiness check, rather than a client of its own");
        }

        await using (var unregistered = HostWith(services => ReplaceObjectStorageCheck(services, check: null)))
        {
            (await SystemHealthAsync(unregistered, admin)).GetProperty("objectStorage").GetProperty("reachable")
                .GetBoolean().Should().BeFalse("a check that never ran is not a healthy one");
        }
    }

    [Fact]
    public async Task An_object_store_that_never_answers_costs_the_dashboard_five_seconds_and_no_more()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        await using var host = HostWith(services => ReplaceObjectStorageCheck(services, new HangingCheck()));
        var client = SignedInOn(host, admin);

        var clock = Stopwatch.StartNew();
        var health = await SystemHealthOfAsync(client);
        clock.Stop();

        health.GetProperty("objectStorage").GetProperty("reachable").GetBoolean().Should().BeFalse();
        clock.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(4.5), "the ping is given its five seconds");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(9),
            "the ping gives up at five seconds even though the check ignores its cancellation");
    }

    private static RecurringJobRowDto Ran(string id, DateTimeOffset last, string state) =>
        new(id, true, "*", last, null, state);

    private static RecurringJobRowDto NeverRan(string id, DateTimeOffset next) =>
        new(id, true, "*", null, next, null);

    private static Dictionary<string, string> Verdicts(JsonElement health) =>
        health.GetProperty("jobs").GetProperty("jobs").EnumerateArray().ToDictionary(
            r => r.GetProperty("id").GetString()!, r => r.GetProperty("verdict").GetString()!);

    private static Dictionary<string, (int Active, int Inactive)> ReferenceLists(JsonElement health) =>
        health.GetProperty("referenceLists").EnumerateArray().ToDictionary(
            r => r.GetProperty("table").GetString()!,
            r => (r.GetProperty("active").GetInt32(), r.GetProperty("inactive").GetInt32()));

    private static int Delta(JsonElement before, JsonElement after, string property) =>
        after.GetProperty(property).GetInt32() - before.GetProperty(property).GetInt32();

    private static Job Email() =>
        Job.FromExpression<EmailJobs>(j => j.SendVerificationEmailAsync(Guid.Empty, CancellationToken.None));

    private static OutboxMessage Message(OutboxSyncStatus status, DateTimeOffset createdAt) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = "DashboardSystemHealthTest",
        PayloadJson = "{}",
        CreatedAt = createdAt,
        SyncStatus = status,
    };

    // A draft tender with one uploaded specification, through the routes a procurement officer uses. A tender file
    // is scanned when it is first asked for rather than by a background job, so it stays pending here, and the test
    // only has to make it old.
    private async Task<(string ReferenceCode, Guid AttachmentId)> TenderAttachmentAsync()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var create = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = "Dashboard stuck scan RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(2),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var referenceCode = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.4 tender specification"u8.ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "specification.pdf");
        (await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/attachments", content))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attachmentId = await db.Set<RfqAttachment>()
            .Where(a => db.Rfqs.Any(r => r.Id == a.RfqId && r.ReferenceCode == referenceCode))
            .Select(a => a.Id).SingleAsync();

        return (referenceCode, attachmentId);
    }

    private static void ReplaceObjectStorageCheck(IServiceCollection services, IHealthCheck? check) =>
        services.Configure<HealthCheckServiceOptions>(options =>
        {
            var existing = options.Registrations.Single(r => r.Name == ObjectStorageCheck);
            options.Registrations.Remove(existing);

            if (check is not null)
            {
                options.Registrations.Add(new HealthCheckRegistration(ObjectStorageCheck, check, null, existing.Tags));
            }
        });

    private WebApplicationFactory<Program> HostWith(
        Action<IServiceCollection> overrides, params (string Key, string Value)[] settings)
    {
        var fixtureKey = fixture.Services.GetRequiredService<JwtSigningKeyProvider>().GetValidationKey();

        return fixture.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings) builder.UseSetting(key, value);

            builder.ConfigureTestServices(services =>
            {
                overrides(services);

                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                    options => options.TokenValidationParameters.IssuerSigningKey = fixtureKey);
            });
        });
    }

    private static HttpClient SignedInOn(WebApplicationFactory<Program> host, HttpClient signedIn)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        return client;
    }

    private Task<JsonElement> SystemHealthAsync(HttpClient admin) => SystemHealthOfAsync(admin);

    private static Task<JsonElement> SystemHealthAsync(WebApplicationFactory<Program> host, HttpClient admin) =>
        SystemHealthOfAsync(SignedInOn(host, admin));

    private static async Task<JsonElement> SystemHealthOfAsync(HttpClient client)
    {
        var response = await client.GetAsync(Route);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);

        var section = JsonDocument.Parse(text).RootElement.GetProperty("systemHealth");
        section.GetProperty("status").GetString().Should().Be("ok", text);
        return section.GetProperty("data");
    }

    private sealed class ScriptedJobsMonitor : IGetJobsMonitorHandler
    {
        public Func<DateTimeOffset, IReadOnlyList<RecurringJobRowDto>> Script { get; set; } = _ => [];

        public bool Enabled { get; set; } = true;

        public JobsMonitorDto Handle() => new(Enabled, Script(DateTimeOffset.UtcNow));
    }

    // A failed state with the failure time the test chooses, so a failure from eight days ago can be seeded. The
    // scheduler reads the failure time back from exactly these fields.
    private sealed class FailedAt(DateTime at) : IState
    {
        public string Name => FailedState.StateName;

        public string Reason => "Seeded by the dashboard's system health tests";

        public bool IsFinal => false;

        public bool IgnoreJobLoadException => false;

        public Dictionary<string, string> SerializeData() => new()
        {
            ["FailedAt"] = JobHelper.SerializeDateTime(at),
            ["ExceptionType"] = "System.InvalidOperationException",
            ["ExceptionMessage"] = "seeded",
            ["ExceptionDetails"] = "seeded",
        };
    }

    private sealed class SendingTransport : IOutboxTransport
    {
        public Task SendAsync(Guid messageId, string type, string payloadJson, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class CallCounter
    {
        private int _count;

        public int Count => _count;

        public void Add() => Interlocked.Increment(ref _count);
    }

    private sealed class FixedCheck(HealthStatus status, CallCounter calls) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            calls.Add();
            return Task.FromResult(new HealthCheckResult(status));
        }
    }

    // Never answers, and does not listen to its cancellation either, like a client stuck in a read without a timeout.
    private sealed class HangingCheck : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
            return HealthCheckResult.Healthy();
        }
    }
}
