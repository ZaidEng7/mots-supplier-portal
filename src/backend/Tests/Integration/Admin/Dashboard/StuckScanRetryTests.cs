// The operator's re-queue of supplier-document virus scans that stalled: POST /api/v1/admin/scans/retry.
//
// Every test runs through the API on the fixture's host, against the real database, the real job storage, the real
// object store and the real scanner. The fixture's host runs a job server for the whole suite, so a scan this
// request queues is soon picked up and run. What the request did is read back from the job table: a new scan job in
// any state but deleted or failed is a scan that was queued.
//
// A scan that must stay queued or running for a test is made where that server cannot reach it: queued on a queue
// it does not listen to, or written straight into the running state without passing through a queue.
//
//
// WHAT IS CHECKED
//
// The gate, both ways: a staff account without admin.users.manage is refused, and a system administrator is not.
//
// A stale pending document gets exactly one new scan queued and one document_scan_requeued row naming the caller,
// and the supplier's own trail does not show that row. Its failed and scheduled scan jobs are deleted rather than
// left beside the new one. A document pending for less than the threshold, and one whose scan is already queued or
// running, get nothing. A document whose quarantine file is gone is named in the answer and gets nothing.
//
// The cap and the order are checked together and cheaply: one more stale document than a batch holds, their upload
// times running backwards against the order they were inserted in, so the one left out is the newest and is also
// the first row written. A batch taken newest first, or in insertion order, or without the cap, leaves out a
// different one or none.
//
// Documents whose quarantine file is gone do not take up the batch: a whole batch of them ahead of a document that
// can be requeued does not stop that one being requeued.
//
// Two presses at the same moment on a document that never had a scan job queue one scan and write one row between
// them, because the second waits for the first.
//
// When the job server fails part way through a press, which a derived host stands in for with a job client that fails
// on its second job, the scan already queued keeps its audit row and the failure still reaches the caller.
//
// Tender attachments and bid files are pending too until first downloaded, and backdating them must not bring them
// into the batch: they gain no job, no audit row and no change of state.
//
//
// THE SHARED DATABASE
//
// Every document here is backdated into the year 2000, so it is older than anything another class leaves pending
// and lands at the front of the batch whatever else is in the table. The figures are asserted on these documents'
// own identifiers and rows, never on table totals, except the still-pending figure, which is asserted as a lower
// bound. A request here may also re-queue a stale document another class left behind, which is what the endpoint is
// for and harms nothing. Each test deletes the scan jobs it made by hand, waits for the scans its request queued to
// finish, and then removes its documents and their files, wherever the scan left them. Audit rows stay, because the
// audit table is append-only, and so do the accounts and the seeded bid, each under its own unique name.

namespace MotsSupplierPortal.Tests.Integration.Admin.Dashboard;

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class StuckScanRetryTests(PostgresApiFixture fixture)
{
    private const string Route = "/api/v1/admin/scans/retry";
    private const string Requeued = "document_scan_requeued";
    private const string IdleQueue = "scan_retry_idle";

    private static readonly DateTimeOffset LongAgo = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] Queued =
        [EnqueuedState.StateName, ProcessingState.StateName, SucceededState.StateName];

    private sealed record Seeded(Guid Id, string ReferenceCode, string Key);

    private sealed record JobRow(string JobId, string State);

    private sealed record Answer(int Requeued, int StillPending, List<string> QuarantineFileMissing);

    private async Task<(HttpClient Supplier, Guid SupplierId, Guid TypeId)> SupplierAsync()
    {
        var name = $"Scan Retry {Guid.NewGuid():N}"[..26];
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplierId = await db.Suppliers.Where(s => s.DisplayNameEn == name).Select(s => s.Id).SingleAsync();
        var typeId = await db.DocumentTypes.Where(t => t.Code == "commercial_registration").Select(t => t.Id).SingleAsync();
        return (client, supplierId, typeId);
    }

    // Pending documents uploaded at the given times, written in the order given, each with its quarantine file
    // stored unless withFile says otherwise.
    private async Task<List<Seeded>> PendingDocumentsAsync(
        Guid supplierId, Guid typeId, IReadOnlyList<DateTimeOffset> uploadedAt, bool withFile = true)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        var seeded = new List<Seeded>();
        foreach (var _ in uploadedAt)
        {
            var key = $"quarantine/scan-retry/{Guid.NewGuid():N}.pdf";
            if (withFile)
            {
                await storage.SaveAsync(key, new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4 pending")),
                    "application/pdf", CancellationToken.None);
            }

            var document = SupplierDocument.CreatePendingScan(
                $"DOC-RQ-{Guid.NewGuid():N}"[..20], supplierId, typeId, 1, key, "licence.pdf", "application/pdf", 16,
                Guid.CreateVersion7(), issueDate: null, expiryDate: today.AddYears(1), expiryTracked: true, today: today);
            db.SupplierDocuments.Add(document);
            await db.SaveChangesAsync();
            seeded.Add(new Seeded(document.Id, document.ReferenceCode, key));
        }

        for (var i = 0; i < seeded.Count; i++)
        {
            var id = seeded[i].Id;
            var at = uploadedAt[i];
            await db.SupplierDocuments.Where(d => d.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(d => d.UploadedAt, at));
        }

        return seeded;
    }

    private async Task<Answer> RetryAsync(HttpClient admin)
    {
        var response = await admin.PostAsync(Route, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Answer>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    // Read straight from the job table rather than through the monitoring lists the handler reads, so a handler that
    // misread those lists cannot agree with its own test.
    private async Task<List<JobRow>> ScanJobsOfAsync(Guid id)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pattern = $"%{id}%";
        return await db.Database
            .SqlQuery<JobRow>($"SELECT id::text AS \"JobId\", statename AS \"State\" FROM hangfire.job WHERE arguments::text LIKE {pattern} ORDER BY id")
            .ToListAsync();
    }

    private async Task<List<Domain.Audit.AuditLog>> RequeueRowsOfAsync(Guid id)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking().Where(a => a.AggregateId == id && a.Action == Requeued).ToListAsync();
    }

    // The job server builds this state itself when a worker picks a job up, and keeps its constructor to itself. A
    // running scan the fixture's server will not also pick up can only be had by building the state the same way.
    private static ProcessingState RunningOnAWorker() =>
        (ProcessingState)Activator.CreateInstance(
            typeof(ProcessingState), BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
            args: ["scan-retry-test", "worker-1"], culture: null)!;

    private WebApplicationFactory<Program> HostWith(Action<IServiceCollection> overrides)
    {
        var fixtureKey = fixture.Services.GetRequiredService<JwtSigningKeyProvider>().GetValidationKey();

        return fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            overrides(services);

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.TokenValidationParameters.IssuerSigningKey = fixtureKey);
        }));
    }

    // The real job client, except that the second job it is asked to create fails, as a job server that stopped
    // answering part way through a press would.
    private sealed class FailingOnSecondCreate(IBackgroundJobClient inner) : IBackgroundJobClient
    {
        private int _creates;

        public string Create(Job job, IState state) =>
            Interlocked.Increment(ref _creates) == 2
                ? throw new InvalidOperationException("The job server stopped answering.")
                : inner.Create(job, state);

        public bool ChangeState(string jobId, IState state, string expectedState) =>
            inner.ChangeState(jobId, state, expectedState);
    }

    private IBackgroundJobClient Jobs() => fixture.Services.GetRequiredService<IBackgroundJobClient>();

    // The jobs made by hand go first, so the wait that follows is only for the scans the request queued, which the job
    // server runs to an end on its own. Removing a document under a running scan would fail that scan and send it
    // round its retries.
    private async Task ForgetAsync(IReadOnlyList<Seeded> documents, params string[] madeByHand)
    {
        var jobs = Jobs();
        foreach (var jobId in madeByHand) jobs.Delete(jobId);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        foreach (var document in documents)
        {
            while (DateTimeOffset.UtcNow < deadline
                   && (await ScanJobsOfAsync(document.Id)).Any(job =>
                       job.State is "Enqueued" or "Processing" && !madeByHand.Contains(job.JobId)))
            {
                await Task.Delay(200);
            }
        }

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        foreach (var document in documents)
        {
            foreach (var job in await ScanJobsOfAsync(document.Id))
            {
                if (job.State is not ("Deleted" or "Succeeded")) jobs.Delete(job.JobId);
            }

            var keyNow = await db.SupplierDocuments.Where(d => d.Id == document.Id)
                .Select(d => d.StorageKey).SingleOrDefaultAsync();
            foreach (var key in new[] { document.Key, keyNow }.OfType<string>().Distinct())
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }

            await db.SupplierDocuments.Where(d => d.Id == document.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Only_admin_users_manage_may_requeue()
    {
        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.OnboardingReviewer, Roles.MinistryViewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.PostAsync(Route, null)).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{role} does not hold admin.users.manage");
        }

        var (supplier, _, _) = await SupplierAsync();
        (await supplier.PostAsync(Route, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.CreateRawClient().PostAsync(Route, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        (await admin.PostAsync(Route, null)).StatusCode.Should().Be(HttpStatusCode.OK, "the control");
    }

    [Fact]
    public async Task A_stale_document_gets_one_new_scan_and_an_audit_row_naming_the_caller_and_its_dead_jobs_go()
    {
        var (admin, adminId) = await StaffTestClient.CreateWithMfaAndIdAsync(fixture, Roles.SystemAdmin);
        var (supplier, supplierId, typeId) = await SupplierAsync();
        var seeded = await PendingDocumentsAsync(supplierId, typeId, [LongAgo]);
        var document = seeded[0];
        var jobs = Jobs();

        // A failed state given to a fresh job is turned into a retry by the job server's retry rule, so the job is
        // made as one that has already been retried ten times, which is how a scan comes to be left failed.
        var failed = ((IBackgroundJobClientV2)jobs).Create(
            Job.FromExpression<DocumentScanJob>(job => job.ScanAsync(document.Id, CancellationToken.None)),
            new FailedState(new InvalidOperationException("scanner down")),
            new Dictionary<string, object?> { ["RetryCount"] = 10 });
        var retrying = jobs.Schedule<DocumentScanJob>(
            job => job.ScanAsync(document.Id, CancellationToken.None), TimeSpan.FromHours(1));
        try
        {
            (await ScanJobsOfAsync(document.Id)).Should().BeEquivalentTo(
                [new JobRow(failed, FailedState.StateName), new JobRow(retrying, ScheduledState.StateName)],
                "the precondition: one dead scan in each state the re-queue clears");

            var answer = await RetryAsync(admin);

            answer.Requeued.Should().BeGreaterThanOrEqualTo(1);
            answer.QuarantineFileMissing.Should().NotContain(document.ReferenceCode);

            var after = await ScanJobsOfAsync(document.Id);
            after.Should().ContainSingle(j => j.JobId == failed).Which.State.Should().Be(DeletedState.StateName,
                "a failed scan is deleted, not left beside the new one");
            after.Should().ContainSingle(j => j.JobId == retrying).Which.State.Should().Be(DeletedState.StateName,
                "a scan waiting to retry would scan the document a second time later");
            after.Where(j => j.JobId != failed && j.JobId != retrying).Should().ContainSingle("exactly one new scan is queued")
                .Which.State.Should().BeOneOf(Queued);

            var rows = await RequeueRowsOfAsync(document.Id);
            rows.Should().ContainSingle();
            rows[0].ActorUserId.Should().Be(adminId, "the row names the administrator who pressed the button");
            rows[0].AggregateType.Should().Be("SupplierDocument");
            rows[0].ReferenceCode.Should().Be(document.ReferenceCode);

            var trail = await supplier.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me/audit?pageSize=100");
            trail.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("action").GetString())
                .Should().NotContain(Requeued, "staff work on the scanning queue stays out of the supplier's own trail");
        }
        finally
        {
            await ForgetAsync(seeded);
        }
    }

    [Fact]
    public async Task A_document_pending_for_less_than_the_threshold_is_left_alone()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (_, supplierId, typeId) = await SupplierAsync();
        var justUnder = DateTimeOffset.UtcNow - StuckScans.PendingLongerThan + TimeSpan.FromMinutes(1);
        var seeded = await PendingDocumentsAsync(supplierId, typeId, [justUnder, LongAgo]);
        try
        {
            await RetryAsync(admin);

            (await ScanJobsOfAsync(seeded[0].Id)).Should().BeEmpty("fourteen minutes is not stuck");
            (await RequeueRowsOfAsync(seeded[0].Id)).Should().BeEmpty();

            (await ScanJobsOfAsync(seeded[1].Id)).Should().ContainSingle("the control: the stale one beside it is requeued")
                .Which.State.Should().BeOneOf(Queued);
        }
        finally
        {
            await ForgetAsync(seeded);
        }
    }

    [Theory]
    [InlineData("Enqueued")]
    [InlineData("Processing")]
    public async Task A_document_whose_scan_is_queued_or_running_is_skipped(string state)
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (_, supplierId, typeId) = await SupplierAsync();
        var seeded = await PendingDocumentsAsync(supplierId, typeId, [LongAgo, LongAgo.AddSeconds(1)]);
        var document = seeded[0];
        var existing = Jobs().Create<DocumentScanJob>(
            job => job.ScanAsync(document.Id, CancellationToken.None),
            state == EnqueuedState.StateName ? new EnqueuedState(IdleQueue) : RunningOnAWorker());
        try
        {
            var answer = await RetryAsync(admin);

            (await ScanJobsOfAsync(document.Id)).Should().ContainSingle()
                .Which.Should().Be(new JobRow(existing, state), "a scan on its way is neither deleted nor doubled");
            (await RequeueRowsOfAsync(document.Id)).Should().BeEmpty();
            answer.StillPending.Should().BeGreaterThanOrEqualTo(1, "the skipped document is still pending");

            (await RequeueRowsOfAsync(seeded[1].Id)).Should().ContainSingle("the control: the one beside it is requeued");
        }
        finally
        {
            await ForgetAsync(seeded, existing);
        }
    }

    [Fact]
    public async Task A_document_whose_quarantine_file_is_gone_is_reported_not_requeued()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (_, supplierId, typeId) = await SupplierAsync();
        var missing = await PendingDocumentsAsync(supplierId, typeId, [LongAgo], withFile: false);
        var present = await PendingDocumentsAsync(supplierId, typeId, [LongAgo.AddSeconds(1)]);
        try
        {
            var answer = await RetryAsync(admin);

            answer.QuarantineFileMissing.Should().Contain(missing[0].ReferenceCode);
            answer.QuarantineFileMissing.Should().NotContain(present[0].ReferenceCode);
            answer.StillPending.Should().BeGreaterThanOrEqualTo(1);
            (await ScanJobsOfAsync(missing[0].Id)).Should().BeEmpty("a scan of a file that is not there fails ten times for nothing");
            (await RequeueRowsOfAsync(missing[0].Id)).Should().BeEmpty();

            (await ScanJobsOfAsync(present[0].Id)).Should().ContainSingle("the control: the one with its file is requeued")
                .Which.State.Should().BeOneOf(Queued);
        }
        finally
        {
            await ForgetAsync([.. missing, .. present]);
        }
    }

    [Fact]
    public async Task One_press_takes_at_most_a_batch_and_the_oldest_first()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (_, supplierId, typeId) = await SupplierAsync();

        // Written newest first, so the newest is also the first row written and has the lowest identifier.
        var uploadedAt = Enumerable.Range(0, StuckScans.BatchSize + 1)
            .Select(i => LongAgo.AddMinutes(StuckScans.BatchSize - i))
            .ToList();
        var seeded = await PendingDocumentsAsync(supplierId, typeId, uploadedAt);
        try
        {
            var answer = await RetryAsync(admin);

            answer.Requeued.Should().Be(StuckScans.BatchSize,
                "these are the oldest stuck documents in the table, so they fill the batch on their own");
            answer.StillPending.Should().BeGreaterThanOrEqualTo(1, "the one past the batch is still pending");

            (await ScanJobsOfAsync(seeded[0].Id)).Should().BeEmpty("the newest of them is the one past the batch");
            (await RequeueRowsOfAsync(seeded[0].Id)).Should().BeEmpty();
            foreach (var document in seeded.Skip(1))
            {
                (await RequeueRowsOfAsync(document.Id)).Should().ContainSingle();
            }
        }
        finally
        {
            await ForgetAsync(seeded);
        }
    }

    [Fact]
    public async Task Documents_whose_file_is_gone_do_not_fill_the_batch_ahead_of_one_that_can_be_requeued()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (_, supplierId, typeId) = await SupplierAsync();

        // A whole batch of the oldest stuck documents have lost their file, and one more, newer than all of them and
        // with its file, sits just past the first batch.
        var missing = await PendingDocumentsAsync(supplierId, typeId,
            Enumerable.Range(0, StuckScans.BatchSize).Select(i => LongAgo.AddMinutes(i)).ToList(), withFile: false);
        var present = await PendingDocumentsAsync(supplierId, typeId, [LongAgo.AddDays(1)]);
        try
        {
            var answer = await RetryAsync(admin);

            answer.QuarantineFileMissing.Should().Contain(missing.Select(d => d.ReferenceCode));
            (await RequeueRowsOfAsync(present[0].Id)).Should().ContainSingle(
                "the documents that cannot be requeued are passed over and the call goes on to the one that can");
            (await ScanJobsOfAsync(present[0].Id)).Should().ContainSingle().Which.State.Should().BeOneOf(Queued);
            foreach (var document in missing)
            {
                (await ScanJobsOfAsync(document.Id)).Should().BeEmpty();
            }
        }
        finally
        {
            await ForgetAsync([.. missing, .. present]);
        }
    }

    [Fact]
    public async Task Two_presses_at_once_queue_one_scan_for_a_document_that_had_none()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (_, supplierId, typeId) = await SupplierAsync();
        var seeded = await PendingDocumentsAsync(supplierId, typeId, [LongAgo]);
        var document = seeded[0];
        try
        {
            (await ScanJobsOfAsync(document.Id)).Should().BeEmpty("the precondition: no scan was ever queued");

            await Task.WhenAll(RetryAsync(admin), RetryAsync(admin));

            (await ScanJobsOfAsync(document.Id)).Should().ContainSingle(
                "the second press waits for the first and then sees its scan on the way");
            (await RequeueRowsOfAsync(document.Id)).Should().ContainSingle();
        }
        finally
        {
            await ForgetAsync(seeded);
        }
    }

    [Fact]
    public async Task When_queuing_fails_part_way_the_scans_already_queued_keep_their_audit_rows()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var (_, supplierId, typeId) = await SupplierAsync();
        var seeded = await PendingDocumentsAsync(supplierId, typeId, [LongAgo, LongAgo.AddSeconds(1)]);
        try
        {
            await using var host = HostWith(services => services.AddSingleton<IBackgroundJobClient>(sp =>
                new FailingOnSecondCreate(new BackgroundJobClient(sp.GetRequiredService<JobStorage>()))));
            var client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;

            (await client.PostAsync(Route, null)).StatusCode.Should().Be(HttpStatusCode.InternalServerError,
                "the failure still reaches the caller");

            (await ScanJobsOfAsync(seeded[0].Id)).Should().ContainSingle("the first scan was queued before the failure");
            (await RequeueRowsOfAsync(seeded[0].Id)).Should().ContainSingle(
                "a scan that was queued keeps the record of who queued it");
            (await ScanJobsOfAsync(seeded[1].Id)).Should().BeEmpty();
            (await RequeueRowsOfAsync(seeded[1].Id)).Should().BeEmpty("nothing was queued for the second");
        }
        finally
        {
            await ForgetAsync(seeded);
        }
    }

    [Fact]
    public async Task Tender_attachments_and_bid_files_are_untouched()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var bid = await EvaluationSeed.CreateAsync(fixture, "Scan Retry Bid", withDocuments: true);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rfqId = await db.Rfqs.Where(r => r.ReferenceCode == bid.RfqCode).Select(r => r.Id).SingleAsync();

        // The seeded tender carries no attachment, so a row is added to it directly. No file stands behind it,
        // because nothing here reads one.
        var attachment = new RfqAttachment
        {
            Id = Guid.CreateVersion7(), RfqId = rfqId, StorageKey = $"rfq-attachments/scan-retry/{Guid.NewGuid():N}.pdf",
            OriginalFileName = "specification.pdf", ContentType = "application/pdf", UploadedAt = LongAgo,
        };
        db.Add(attachment);
        await db.SaveChangesAsync();
        await db.ProposalDocuments.Where(d => d.Id == bid.TechnicalDocumentId)
            .ExecuteUpdateAsync(set => set.SetProperty(d => d.UploadedAt, LongAgo));
        try
        {
            await RetryAsync(admin);

            foreach (var id in new[] { attachment.Id, bid.TechnicalDocumentId })
            {
                (await ScanJobsOfAsync(id)).Should().BeEmpty("tender and bid files are scanned on first download, not by a job");
                (await RequeueRowsOfAsync(id)).Should().BeEmpty();
            }

            (await db.Set<RfqAttachment>().AsNoTracking().Where(a => a.Id == attachment.Id).Select(a => a.ScanState).SingleAsync())
                .Should().Be(AttachmentScanState.PendingScan);
            (await db.ProposalDocuments.AsNoTracking().Where(d => d.Id == bid.TechnicalDocumentId).Select(d => d.ScanState).SingleAsync())
                .Should().Be(AttachmentScanState.PendingScan);
        }
        finally
        {
            // The attachment row goes. The bid stays, as EvaluationSeed's bids do in every class that uses it, with
            // its upload time put back to now.
            await db.Set<RfqAttachment>().Where(a => a.Id == attachment.Id).ExecuteDeleteAsync();
            await db.ProposalDocuments.Where(d => d.Id == bid.TechnicalDocumentId)
                .ExecuteUpdateAsync(set => set.SetProperty(d => d.UploadedAt, DateTimeOffset.UtcNow));
        }
    }
}
