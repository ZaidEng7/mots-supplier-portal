// A supplier's document scanned while the virus scanner is down is kept, not deleted.
//
// THIS WAS DATA LOSS. The scanner reported any failure as infected, and the scan job deletes an infected file and
// marks the document refused. So a supplier who replaced an expired licence while the scanner was restarting lost the
// file, saw it refused, and was never told the scanner had been the problem.
//
// THE TEST RUNS THE REAL JOB against the real database and the real object storage, with only the scanner replaced by
// one that cannot answer. It asserts what a supplier would see: the document still pending, and the uploaded bytes
// still in quarantine, readable - plus the job failing, which is what makes the job server try it again later.
//
// THE CONTROL runs the same job with a scanner that answers clean, and requires the document to reach review. Without
// it, a job that threw on every answer would pass the outage test.
//
// A SECOND RUN OF THE JOB LEAVES A DOCUMENT IT ALREADY SCANNED ALONE. Failing is what makes the job server run the job
// again, and it runs any job at least once rather than exactly once, so a run can meet a document that is no longer
// pending. These scan a document once and then again with a scanner that counts its calls. The second run has to
// finish without asking the scanner, and leave the document's state, its storage key and its file's bytes as the
// first run left them. The second answer is clean and then infected for a document scanned clean, and clean for a
// document refused. The first version failed every one of those second runs, but not all in the same place. For a
// document scanned clean it asked the scanner and then threw, on moving the clean file onto itself or on the
// document in review turning away an infected verdict. For a refused document it threw on reading the deleted file,
// before the scanner was asked. So for a refused document the scanner's count is zero under both versions, and what
// tells them apart is the second run finishing at all.
//
// In review and refused are the two states the job leads to, but a later run can find a document anywhere past
// PendingScan: approved, rejected or expired by then. So one more theory scans a document clean, writes each state
// but PendingScan onto its row in turn, and runs the job again under the same requirements. The state is written
// onto the row rather than reached through the document's own transitions, because from a document scanned clean
// those cannot reach every state, a refused one for a start, and this theory is about the job rather than about
// them. The states come from the enumeration itself, so a state added later is covered without anyone listing it,
// and a check that skipped only the two states the job leads to fails for every other one.
//
// A TENDER OR BID FILE ASKED FOR DURING AN OUTAGE ANSWERS 503 AND STAYS PENDING. Those files are scanned when first
// downloaded rather than by a job, so the outage reaches the reader directly: a 404 would tell a supplier the tender
// specification is gone. These run through the API on a host whose scanner cannot answer, and the control is the same
// request on the ordinary host afterwards - served, which proves the outage left the file intact and scannable.

namespace MotsSupplierPortal.Tests.Integration.Documents;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ScannerOutageTests(PostgresApiFixture fixture)
{
    private sealed class FixedScanner(ScanOutcome outcome) : IVirusScanner
    {
        private int _calls;

        public int Calls => _calls;

        public Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(outcome);
        }
    }

    private async Task<(Guid DocumentId, string Key)> PendingDocumentAsync()
    {
        var name = $"Outage {Guid.NewGuid():N}"[..24];
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var supplierId = await db.Suppliers.Where(s => s.DisplayNameEn == name).Select(s => s.Id).FirstAsync();
        var typeId = await db.DocumentTypes.Where(t => t.Code == "commercial_registration").Select(t => t.Id).SingleAsync();
        var key = $"quarantine/outage/{Guid.NewGuid():N}.pdf";
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        await storage.SaveAsync(key, new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4 renewed licence")),
            "application/pdf", CancellationToken.None);

        var document = SupplierDocument.CreatePendingScan(
            $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}", supplierId, typeId, 1, key, "licence.pdf",
            "application/pdf", 24, Guid.CreateVersion7(), issueDate: null, expiryDate: today.AddYears(1),
            expiryTracked: true, today: today);
        db.SupplierDocuments.Add(document);
        await db.SaveChangesAsync();

        return (document.Id, key);
    }

    private Task RunScanAsync(Guid documentId, ScanOutcome outcome) => RunScanAsync(documentId, new FixedScanner(outcome));

    private async Task RunScanAsync(Guid documentId, IVirusScanner scanner)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var job = new DocumentScanJob(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IFileStorage>(),
            scanner,
            scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
            NullLogger<DocumentScanJob>.Instance);

        await job.ScanAsync(documentId, CancellationToken.None);
    }

    private async Task<DocumentState> StateOfAsync(Guid documentId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SupplierDocuments.AsNoTracking().Where(d => d.Id == documentId).Select(d => d.State).SingleAsync();
    }

    private async Task<(DocumentState State, string StorageKey)> StoredAsync(Guid documentId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.SupplierDocuments.AsNoTracking().Where(d => d.Id == documentId)
            .Select(d => new { d.State, d.StorageKey }).SingleAsync();
        return (row.State, row.StorageKey);
    }

    private async Task<string> ContentAtAsync(string key)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await using var content = await storage.OpenReadAsync(key, CancellationToken.None);
        return await new StreamReader(content).ReadToEndAsync();
    }

    private async Task ForceStateAsync(Guid documentId, DocumentState state)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.SupplierDocuments.Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(set => set.SetProperty(d => d.State, state));
    }

    // Every test that runs the scan job removes the document it made, so the classes that run after them never meet
    // it: the document row goes, and so does whatever file it still points at. The audit rows the job wrote stay,
    // because the audit table is append-only. The supplier stays too, under its own unique name.
    private async Task ForgetAsync(Guid documentId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var (_, key) = await StoredAsync(documentId);

        await storage.DeleteAsync(key, CancellationToken.None);
        await db.SupplierDocuments.Where(d => d.Id == documentId).ExecuteDeleteAsync();
    }

    // Each host makes its own signing key when none is configured, so a token issued by the fixture is refused by a
    // derived host unless that host is told to validate with the fixture's key. Without this every request below is a
    // 401 and the outage is never reached.
    private WebApplicationFactory<Program> HostWithTheScannerDown()
    {
        var fixtureKey = fixture.Services.GetRequiredService<JwtSigningKeyProvider>().GetValidationKey();

        return fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IVirusScanner>(new FixedScanner(ScanOutcome.Unavailable));
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.TokenValidationParameters.IssuerSigningKey = fixtureKey);
        }));
    }

    private static HttpClient SignedInOn(WebApplicationFactory<Program> host, HttpClient signedIn)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        return client;
    }

    private async Task<(HttpClient Officer, string DownloadUrl, Guid AttachmentId)> DraftTenderWithAttachmentAsync()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var create = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = "Outage RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(2),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var referenceCode = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("tender specification bytes"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "specification.pdf");
        (await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/attachments", content))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attachmentId = await db.Set<RfqAttachment>()
            .Where(a => db.Rfqs.Any(r => r.Id == a.RfqId && r.ReferenceCode == referenceCode))
            .Select(a => a.Id).SingleAsync();

        return (officer, $"/api/v1/rfqs/{referenceCode}/attachments/{attachmentId}/download-url", attachmentId);
    }

    private async Task<AttachmentScanState> AttachmentScanStateAsync(Guid attachmentId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<RfqAttachment>().AsNoTracking().Where(a => a.Id == attachmentId).Select(a => a.ScanState).SingleAsync();
    }

    private async Task<AttachmentScanState> BidDocumentScanStateAsync(Guid documentId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProposalDocuments.AsNoTracking().Where(d => d.Id == documentId).Select(d => d.ScanState).SingleAsync();
    }

    [Fact]
    public async Task A_document_scanned_during_an_outage_stays_pending_with_its_file_kept()
    {
        var (documentId, key) = await PendingDocumentAsync();
        try
        {
            var scan = () => RunScanAsync(documentId, ScanOutcome.Unavailable);

            await scan.Should().ThrowAsync<VirusScannerUnavailableException>(
                "the job fails so the job server tries it again once the scanner is back");
            (await StateOfAsync(documentId)).Should().Be(
                DocumentState.PendingScan, "the first version marked it refused, as if a virus had been found");
            (await ContentAtAsync(key)).Should().Be(
                "%PDF-1.4 renewed licence", "the first version deleted the supplier's upload");
        }
        finally
        {
            await ForgetAsync(documentId);
        }
    }

    [Fact]
    public async Task The_same_document_scanned_clean_reaches_review()
    {
        var (documentId, _) = await PendingDocumentAsync();
        try
        {
            await RunScanAsync(documentId, ScanOutcome.Clean);

            (await StateOfAsync(documentId)).Should().Be(DocumentState.UnderReview);
        }
        finally
        {
            await ForgetAsync(documentId);
        }
    }

    [Theory]
    [InlineData(ScanOutcome.Clean)]
    [InlineData(ScanOutcome.Infected)]
    public async Task A_second_scan_of_a_document_already_scanned_clean_changes_nothing(ScanOutcome secondAnswer)
    {
        var (documentId, _) = await PendingDocumentAsync();
        try
        {
            await RunScanAsync(documentId, ScanOutcome.Clean);
            var scanned = await StoredAsync(documentId);
            scanned.State.Should().Be(DocumentState.UnderReview, "the first run scans it clean");
            var scanner = new FixedScanner(secondAnswer);

            await RunScanAsync(documentId, scanner);

            scanner.Calls.Should().Be(0, "a document that is no longer pending is not scanned again");
            (await StoredAsync(documentId)).Should().Be(scanned, "the second run leaves the document as the first left it");
            (await ContentAtAsync(scanned.StorageKey)).Should().Be(
                "%PDF-1.4 renewed licence", "the second run leaves the supplier's scanned file where it is");
        }
        finally
        {
            await ForgetAsync(documentId);
        }
    }

    [Fact]
    public async Task A_second_scan_of_a_refused_document_changes_nothing()
    {
        var (documentId, _) = await PendingDocumentAsync();
        try
        {
            await RunScanAsync(documentId, ScanOutcome.Infected);
            var scanner = new FixedScanner(ScanOutcome.Clean);

            await RunScanAsync(documentId, scanner);

            scanner.Calls.Should().Be(0, "the run stops once it has read the row, before the file or the scanner");
            (await StateOfAsync(documentId)).Should().Be(DocumentState.ScanRejected);
        }
        finally
        {
            await ForgetAsync(documentId);
        }
    }

    [Theory]
    [MemberData(nameof(StatesPastPendingScan))]
    public async Task A_second_scan_of_a_document_in_any_state_past_pending_changes_nothing(DocumentState state)
    {
        var (documentId, _) = await PendingDocumentAsync();
        try
        {
            await RunScanAsync(documentId, ScanOutcome.Clean);
            var cleanKey = (await StoredAsync(documentId)).StorageKey;
            await ForceStateAsync(documentId, state);
            var scanner = new FixedScanner(ScanOutcome.Clean);

            await RunScanAsync(documentId, scanner);

            scanner.Calls.Should().Be(0, $"a document that is {state} is past PendingScan and is not scanned again");
            (await StoredAsync(documentId)).Should().Be((state, cleanKey), "the second run leaves the document as it found it");
            (await ContentAtAsync(cleanKey)).Should().Be(
                "%PDF-1.4 renewed licence", "the second run leaves the supplier's scanned file where it is");
        }
        finally
        {
            await ForgetAsync(documentId);
        }
    }

    public static TheoryData<DocumentState> StatesPastPendingScan()
    {
        var data = new TheoryData<DocumentState>();
        foreach (var state in Enum.GetValues<DocumentState>().Where(s => s != DocumentState.PendingScan)) data.Add(state);
        return data;
    }

    [Fact]
    public async Task A_tender_attachment_asked_for_during_an_outage_answers_try_again_and_stays_pending()
    {
        var (officer, downloadUrl, attachmentId) = await DraftTenderWithAttachmentAsync();
        await using var outage = HostWithTheScannerDown();

        var during = await SignedInOn(outage, officer).GetAsync(downloadUrl);

        during.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "the first version answered 404, as if the tender specification were gone");
        (await AttachmentScanStateAsync(attachmentId)).Should().Be(AttachmentScanState.PendingScan);

        (await officer.GetAsync(downloadUrl)).StatusCode.Should().Be(HttpStatusCode.OK,
            "the same file is served once the scanner answers");
    }

    [Fact]
    public async Task A_bid_document_asked_for_during_an_outage_answers_try_again_to_the_bidder_and_the_evaluator()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "Outage Bid", withDocuments: true);
        await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { seeded.EvaluatorId } });
        var bidderUrl = $"/api/v1/proposals/{seeded.ProposalCode}/documents/{seeded.TechnicalDocumentId}/download-url";
        var evaluatorUrl =
            $"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/proposals/{seeded.ProposalCode}/documents/{seeded.TechnicalDocumentId}/download-url";
        await using var outage = HostWithTheScannerDown();

        (await SignedInOn(outage, seeded.Supplier).GetAsync(bidderUrl)).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);
        (await SignedInOn(outage, seeded.Evaluator).GetAsync(evaluatorUrl)).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BidDocumentScanStateAsync(seeded.TechnicalDocumentId)).Should().Be(AttachmentScanState.PendingScan);

        (await seeded.Evaluator.GetAsync(evaluatorUrl)).StatusCode.Should().Be(HttpStatusCode.OK,
            "the same file is served once the scanner answers");
    }
}
