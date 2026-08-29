using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// BRULE-025 and FR-NOT-006 (MSP-68): renewal reminders escalate on a cadence, and are
/// de-duplicated.
///
/// <para><b>Why these run the job repeatedly.</b> Escalation is a claim about what happens across
/// runs; a single invocation cannot distinguish "escalates" from "fires once and stops", which is
/// exactly what the previous implementation did. Every test here drives the real job against the
/// real database more than once, and asserts on the emails that accumulated.</para>
///
/// <para><b>Why de-duplication is not keyed on the run.</b> The job will run more than once a day -
/// retries, restarts, manual triggers, schedule changes. So the first test runs it twice on the same
/// simulated day and requires the second run to be silent, which a "have we run today" check would
/// pass for the wrong reason and a per-run guard would fail outright.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentRenewalReminderTests(PostgresApiFixture fixture)
{
    /// <summary>Captures what the job enqueued instead of running it. Hangfire's Enqueue&lt;T&gt; is
    /// an extension over Create(Job, IState), so recording there catches every call regardless of
    /// which helper the job used.</summary>
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

    /// <summary>Builds the job over the fixture's real database with a recording queue. The cadence
    /// is passed in so tests can state it rather than depend on the default staying 30/14/3.</summary>
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
            db, scope.ServiceProvider.GetRequiredService<IAuditLogger>(), jobs, configuration);

        return new Harness(scope, db, jobs, job);
    }

    /// <summary>A verified supplier holding one approved, expiry-tracked document that expires in
    /// <paramref name="daysFromToday"/> days. Built through the real transitions rather than by
    /// forcing state, so the row is one the production code could actually have produced.</summary>
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
            supplierId, typeId, version, "quarantine/key", fileName, "application/pdf", 2048,
            Guid.CreateVersion7(), issueDate: null, expiryDate: today.AddDays(daysFromToday),
            expiryTracked: true, today: today);

        document.MarkScanClean("clean/key");
        document.Approve(Guid.CreateVersion7());

        db.SupplierDocuments.Add(document);
        await db.SaveChangesAsync();

        return (supplierId, document);
    }

    /// <summary>
    /// Counts reminders for ONE document, identified by its filename.
    ///
    /// Counting every enqueued email instead looked simpler and was wrong: these tests share one
    /// database, the job deliberately processes every document in it, and so each test's total
    /// included documents seeded by its neighbours. The suite passed one test at a time and failed
    /// as a suite - a green that depended on execution order, which is the same family of defect
    /// this ticket is about. Filenames are unique per seed so each assertion can name its subject.
    /// </summary>
    private static int ExpiringEmailsFor(RecordingJobClient jobs, string fileName) =>
        jobs.Enqueued.Count(e =>
            e.Method == nameof(Infrastructure.Email.EmailJobs.SendDocumentExpiringEmailAsync)
            && e.Args.Length > 1 && (string?)e.Args[1] == fileName);

    [Fact]
    public async Task Running_the_job_again_on_the_same_day_does_not_re_notify()
    {
        // The load-bearing de-duplication test. The job is invoked twice with nothing changing in
        // between - the second call must be silent, and must be silent because the reminder is
        // already recorded, not because the day has not rolled over.
        var fileName = $"dedupe-{Guid.NewGuid():N}.pdf";
        var (_, document) = await SeedApprovedDocumentAsync(20, fileName);

        var first = CreateJob(30, 14, 3);
        using (first.Scope) await first.Job.RunAsync(CancellationToken.None);

        var second = CreateJob(30, 14, 3);
        using (second.Scope) await second.Job.RunAsync(CancellationToken.None);

        ExpiringEmailsFor(first.Jobs, fileName).Should().Be(1, "the first run crosses the 30-day step");
        ExpiringEmailsFor(second.Jobs, fileName).Should().Be(0,
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
        // Escalation, proven by running the job repeatedly rather than once. Time is advanced by
        // moving the CADENCE rather than the clock: a document 20 days out has crossed a 30-day step,
        // then a 25-day step, then a 20-day step. That is the same arithmetic the job does against a
        // moving today, and it does not require the test to control the system clock.
        var fileName = $"escalate-{Guid.NewGuid():N}.pdf";
        var (_, document) = await SeedApprovedDocumentAsync(20, fileName);

        var emailsPerRun = new List<int>();

        foreach (var cadence in new[] { new[] { 30 }, [30, 25], [30, 25, 20] })
        {
            var run = CreateJob(cadence);
            using (run.Scope) await run.Job.RunAsync(CancellationToken.None);
            emailsPerRun.Add(ExpiringEmailsFor(run.Jobs, fileName));
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
        // The first-deployment and post-outage case: a document three days from expiry has passed
        // all three steps. Three emails would be absurd, and dripping one per day would chase a
        // deadline that has already effectively arrived. One email, three ledger rows.
        var fileName = $"backlog-{Guid.NewGuid():N}.pdf";
        var (_, document) = await SeedApprovedDocumentAsync(3, fileName);

        var run = CreateJob(30, 14, 3);
        using (run.Scope) await run.Job.RunAsync(CancellationToken.None);

        ExpiringEmailsFor(run.Jobs, fileName).Should().Be(1);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reminders = await db.DocumentExpiryReminders
            .Where(r => r.SupplierDocumentId == document.Id).ToListAsync();

        reminders.Should().HaveCount(3, "the passed steps are recorded so they cannot fire as a backlog");
        reminders.Where(r => r.WasSent).Should().ContainSingle()
            .Which.ThresholdDays.Should().Be(3, "the most urgent step is the one communicated");

        var later = CreateJob(30, 14, 3);
        using (later.Scope) await later.Job.RunAsync(CancellationToken.None);
        ExpiringEmailsFor(later.Jobs, fileName).Should().Be(0, "there is no backlog left to drain");
    }

    [Fact]
    public async Task A_rung_that_falls_due_before_the_document_enters_ExpiringSoon_is_still_sent()
    {
        // The ExpiringSoon window and the reminder ladder are different numbers that coincide only
        // at the shared default of 30. With a window of 14, the 30-day rung falls due while the
        // document is still Approved. It must still be sent: filtering reminders to ExpiringSoon
        // would silently delete the supplier's first reminder whenever someone tightened the window,
        // and nobody would attribute that loss to the setting they changed.
        var fileName = $"narrow-window-{Guid.NewGuid():N}.pdf";
        var (_, document) = await SeedApprovedDocumentAsync(20, fileName);

        var run = CreateJobWithWindow(windowDays: 14, cadence: [30, 14, 3]);
        using (run.Scope) await run.Job.RunAsync(CancellationToken.None);

        ExpiringEmailsFor(run.Jobs, fileName).Should().Be(1,
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
        // The rule the ledger key exists for. A renewed document is not part-way through being
        // chased, so version 2 starts the cadence from the top even though version 1 was already at
        // the 30-day step. Keyed on document + version + threshold, this falls out rather than
        // needing the old reminders to be deleted - and version 1's history survives, which it must,
        // because it records what the supplier was actually told.
        var v1FileName = $"renew-v1-{Guid.NewGuid():N}.pdf";
        var v2FileName = $"renew-v2-{Guid.NewGuid():N}.pdf";
        var (supplierId, first) = await SeedApprovedDocumentAsync(20, v1FileName);

        var initial = CreateJob(30, 14, 3);
        using (initial.Scope) await initial.Job.RunAsync(CancellationToken.None);
        ExpiringEmailsFor(initial.Jobs, v1FileName).Should().Be(1);

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var superseded = await db.SupplierDocuments.SingleAsync(d => d.Id == first.Id);
            superseded.SupersedeWithNewVersion();

            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
            var renewed = SupplierDocument.CreatePendingScan(
                supplierId, superseded.DocumentTypeId, version: 2, "quarantine/key2",
                v2FileName, "application/pdf", 2048, Guid.CreateVersion7(),
                issueDate: null, expiryDate: today.AddDays(20), expiryTracked: true, today: today);

            renewed.MarkScanClean("clean/key2");
            renewed.Approve(Guid.CreateVersion7());

            db.SupplierDocuments.Add(renewed);
            await db.SaveChangesAsync();
        }

        var afterRenewal = CreateJob(30, 14, 3);
        using (afterRenewal.Scope) await afterRenewal.Job.RunAsync(CancellationToken.None);

        ExpiringEmailsFor(afterRenewal.Jobs, v2FileName).Should().Be(1,
            "the new version has no reminder history, so its 30-day step is unsent and fires");

        using var check = fixture.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();

        var oldVersionReminders = await checkDb.DocumentExpiryReminders
            .Where(r => r.SupplierDocumentId == first.Id).ToListAsync();

        oldVersionReminders.Should().ContainSingle(
            "superseding a document does not erase what its supplier was already told");
    }
}
