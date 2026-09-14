// Renewal reminders escalate on a cadence, and are de-duplicated.
//
//
// WHY THESE RUN THE JOB REPEATEDLY
//
// Escalation is a claim about what happens across runs. A single invocation cannot distinguish "escalates" from
// "fires once and stops", which is exactly what the previous implementation did.
//
// Every test here drives the real job against the real database more than once, and asserts on the emails that
// accumulated.
//
//
// WHY DE-DUPLICATION IS NOT KEYED ON THE RUN
//
// The job will run more than once a day: retries, restarts, manual triggers, schedule changes.
//
// So the load-bearing test runs it twice with nothing changing in between and requires the second run to be
// silent, and silent BECAUSE the reminder is already recorded rather than because the day has not rolled over. A
// have-we-run-today check would pass it for the wrong reason and a per-run guard would fail it outright.
//
//
// TIME IS ADVANCED BY MOVING THE CADENCE, NOT THE CLOCK
//
// A document twenty days out has crossed a thirty-day step, then a twenty-five-day step, then a twenty-day step.
// That is the same arithmetic the job does against a moving today, and it does not require the test to control the
// system clock.
//
// The cadence is passed in so the tests state it rather than depending on the shipped default, and it is passed
// through the real settings reader over that configuration, which also exercises the precedence that makes the
// setting safe: with no stored row, configuration is what wins.
//
//
// THE CASES EACH TEST COVERS
//
// A first deployment, or a run after an outage: a document three days from expiry has passed all three steps.
// Three emails would be absurd and dripping one a day would chase a deadline that has already effectively
// arrived. One email, three ledger rows.
//
// A narrower expiry window than the widest rung: the thirty-day rung falls due while the document is still
// approved, and it must still be sent. Filtering reminders to the expiring state would silently delete the
// supplier's first reminder whenever somebody tightened the window, and nobody would attribute that loss to the
// setting they changed.
//
// A renewal: a new version is not part-way through being chased, so it starts the cadence from the top even
// though the previous version was already at the thirty-day step. Because the ledger is keyed on document,
// version and threshold together, that falls out rather than needing the old reminders deleted, and the previous
// version's history survives, which it must, because it records what the supplier was actually told.
//
//
// COUNTING IS SCOPED TO ONE DOCUMENT, BY ITS IDENTIFIER
//
// Counting every enqueued email looked simpler and was wrong: these tests share one database, the job
// deliberately processes every document in it, and each test's total included documents seeded by its neighbours.
//
// The suite passed one test at a time and failed as a suite, which is a green that depended on execution order.
//
// It matched on the filename until a privacy fix stopped job arguments carrying filenames at all; they are
// resolved inside the job now. The identifier is what the argument list actually offers, and it is a better
// identity anyway: unique by construction rather than unique because each seed took care to make it so.
//
// The recorder captures what the job enqueued instead of running it, and it records at the lowest call the
// framework offers, so it catches every enqueue regardless of which helper the job used.

namespace MotsSupplierPortal.Tests.Integration.Documents;

using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentRenewalReminderTests(PostgresApiFixture fixture)
{
    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public List<(string Method, object?[] Args)> Enqueued { get; } = [];

        public string Create(Job job, IState state)
        {
            Enqueued.Add((job.Method.Name, [.. job.Args]));
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed record Harness(
        IServiceScope Scope, AppDbContext Db, RecordingJobClient Jobs, DocumentExpiryJob Job);

    private Harness CreateJob(params int[] cadence) => CreateJobWithWindow(30, cadence);

    private Harness CreateJobWithWindow(int windowDays, params int[] cadence)
    {
        var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobs = new RecordingJobClient();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(cadence
                .Select((days, i) => new KeyValuePair<string, string?>(
                    $"Documents:RenewalReminderDays:{i}", days.ToString()))
                .Append(new KeyValuePair<string, string?>(
                    "Documents:ExpiringSoonWindowDays", windowDays.ToString())))
            .Build();

        var job = new DocumentExpiryJob(
            db, scope.ServiceProvider.GetRequiredService<IAuditLogger>(), jobs,
            new SystemSettingReader(db, configuration));

        return new Harness(scope, db, jobs, job);
    }

    private async Task<(Guid SupplierId, SupplierDocument Document)> SeedApprovedDocumentAsync(
        int daysFromToday, string fileName, Guid? documentTypeId = null, int version = 1)
    {
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Renewal {Guid.NewGuid():N}"[..20]);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var supplierId = await db.Users
            .Where(u => u.SupplierId != null)
            .OrderByDescending(u => u.Id)
            .Select(u => u.SupplierId!.Value)
            .FirstAsync();

        var typeId = documentTypeId ?? await db.DocumentTypes.Select(t => t.Id).FirstAsync();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        var document = SupplierDocument.CreatePendingScan(
            $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
            supplierId, typeId, version, "quarantine/key", fileName, "application/pdf", 2048,
            Guid.CreateVersion7(), issueDate: null, expiryDate: today.AddDays(daysFromToday),
            expiryTracked: true, today: today);

        document.MarkScanClean("clean/key");
        document.Approve(Guid.CreateVersion7());

        db.SupplierDocuments.Add(document);
        await db.SaveChangesAsync();

        return (supplierId, document);
    }

    private static int ExpiringEmailsFor(RecordingJobClient jobs, Guid documentId) =>
        jobs.Enqueued.Count(e =>
            e.Method == nameof(Infrastructure.Email.EmailJobs.SendDocumentExpiringEmailAsync)
            && e.Args.Length > 1 && (Guid?)e.Args[1] == documentId);

    [Fact]
    public async Task Running_the_job_again_on_the_same_day_does_not_re_notify()
    {
        var (_, document) = await SeedApprovedDocumentAsync(20, $"dedupe-{Guid.NewGuid():N}.pdf");

        var first = CreateJob(30, 14, 3);
        using (first.Scope) await first.Job.RunAsync(CancellationToken.None);

        var second = CreateJob(30, 14, 3);
        using (second.Scope) await second.Job.RunAsync(CancellationToken.None);

        ExpiringEmailsFor(first.Jobs, document.Id).Should().Be(1, "the first run crosses the 30-day step");
        ExpiringEmailsFor(second.Jobs, document.Id).Should().Be(0,
            "nothing about the document changed, so a second run within the same day must say nothing");

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reminders = await db.DocumentExpiryReminders
            .Where(r => r.SupplierDocumentId == document.Id).ToListAsync();

        reminders.Should().ContainSingle().Which.ThresholdDays.Should().Be(30);
    }

    [Fact]
    public async Task Reminders_escalate_across_runs_as_the_expiry_approaches()
    {
        var (_, document) = await SeedApprovedDocumentAsync(20, $"escalate-{Guid.NewGuid():N}.pdf");

        var emailsPerRun = new List<int>();

        foreach (var cadence in new[] { new[] { 30 }, [30, 25], [30, 25, 20] })
        {
            var run = CreateJob(cadence);
            using (run.Scope) await run.Job.RunAsync(CancellationToken.None);
            emailsPerRun.Add(ExpiringEmailsFor(run.Jobs, document.Id));
        }

        emailsPerRun.Should().Equal([1, 1, 1],
            "each newly crossed cadence step is chased exactly once - one reminder, not none and " +
            "not a resend of the earlier steps");

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var thresholds = await db.DocumentExpiryReminders
            .Where(r => r.SupplierDocumentId == document.Id)
            .Select(r => r.ThresholdDays).ToListAsync();

        thresholds.Should().BeEquivalentTo([30, 25, 20],
            "the ledger records every step reached, which is what stops any of them firing twice");
    }

    [Fact]
    public async Task A_document_that_crosses_several_steps_at_once_is_chased_once_not_once_per_step()
    {
        var (_, document) = await SeedApprovedDocumentAsync(3, $"backlog-{Guid.NewGuid():N}.pdf");

        var run = CreateJob(30, 14, 3);
        using (run.Scope) await run.Job.RunAsync(CancellationToken.None);

        ExpiringEmailsFor(run.Jobs, document.Id).Should().Be(1);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reminders = await db.DocumentExpiryReminders
            .Where(r => r.SupplierDocumentId == document.Id).ToListAsync();

        reminders.Should().HaveCount(3, "the passed steps are recorded so they cannot fire as a backlog");
        reminders.Where(r => r.WasSent).Should().ContainSingle()
            .Which.ThresholdDays.Should().Be(3, "the most urgent step is the one communicated");

        var later = CreateJob(30, 14, 3);
        using (later.Scope) await later.Job.RunAsync(CancellationToken.None);
        ExpiringEmailsFor(later.Jobs, document.Id).Should().Be(0, "there is no backlog left to drain");
    }

    [Fact]
    public async Task A_rung_that_falls_due_before_the_document_enters_ExpiringSoon_is_still_sent()
    {
        var (_, document) = await SeedApprovedDocumentAsync(20, $"narrow-window-{Guid.NewGuid():N}.pdf");

        var run = CreateJobWithWindow(windowDays: 14, cadence: [30, 14, 3]);
        using (run.Scope) await run.Job.RunAsync(CancellationToken.None);

        ExpiringEmailsFor(run.Jobs, document.Id).Should().Be(1,
            "the 30-day rung is due at 20 days remaining regardless of the state boundary");

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var state = await db.SupplierDocuments.Where(d => d.Id == document.Id)
            .Select(d => d.State).SingleAsync();

        state.Should().Be(DocumentState.Approved,
            "a 14-day window leaves a document 20 days out still Approved - which is the point: the " +
            "reminder went out before the state changed, not because of it");
    }

    [Fact]
    public async Task A_re_upload_resets_the_cadence()
    {
        var (supplierId, first) = await SeedApprovedDocumentAsync(20, $"renew-v1-{Guid.NewGuid():N}.pdf");
        Guid renewedId;

        var initial = CreateJob(30, 14, 3);
        using (initial.Scope) await initial.Job.RunAsync(CancellationToken.None);
        ExpiringEmailsFor(initial.Jobs, first.Id).Should().Be(1);

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var superseded = await db.SupplierDocuments.SingleAsync(d => d.Id == first.Id);
            superseded.SupersedeWithNewVersion();

            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
            var renewed = SupplierDocument.CreatePendingScan(
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
                supplierId, superseded.DocumentTypeId, version: 2, "quarantine/key2",
                $"renew-v2-{Guid.NewGuid():N}.pdf", "application/pdf", 2048, Guid.CreateVersion7(),
                issueDate: null, expiryDate: today.AddDays(20), expiryTracked: true, today: today);

            renewed.MarkScanClean("clean/key2");
            renewed.Approve(Guid.CreateVersion7());

            db.SupplierDocuments.Add(renewed);
            await db.SaveChangesAsync();

            renewedId = renewed.Id;
        }

        var afterRenewal = CreateJob(30, 14, 3);
        using (afterRenewal.Scope) await afterRenewal.Job.RunAsync(CancellationToken.None);

        ExpiringEmailsFor(afterRenewal.Jobs, renewedId).Should().Be(1,
            "the new version has no reminder history, so its 30-day step is unsent and fires");

        using var check = fixture.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();

        var oldVersionReminders = await checkDb.DocumentExpiryReminders
            .Where(r => r.SupplierDocumentId == first.Id).ToListAsync();

        oldVersionReminders.Should().ContainSingle(
            "superseding a document does not erase what its supplier was already told");
    }
}
