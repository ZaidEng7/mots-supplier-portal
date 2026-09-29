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

namespace MotsSupplierPortal.Tests.Integration.Documents;

using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ScannerOutageTests(PostgresApiFixture fixture)
{
    private sealed class FixedScanner(ScanOutcome outcome) : IVirusScanner
    {
        public Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct) => Task.FromResult(outcome);
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

    private async Task RunScanAsync(Guid documentId, ScanOutcome outcome)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var job = new DocumentScanJob(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IFileStorage>(),
            new FixedScanner(outcome),
            scope.ServiceProvider.GetRequiredService<IAuditLogger>());

        await job.ScanAsync(documentId, CancellationToken.None);
    }

    private async Task<DocumentState> StateOfAsync(Guid documentId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SupplierDocuments.AsNoTracking().Where(d => d.Id == documentId).Select(d => d.State).SingleAsync();
    }

    [Fact]
    public async Task A_document_scanned_during_an_outage_stays_pending_with_its_file_kept()
    {
        var (documentId, key) = await PendingDocumentAsync();

        var scan = () => RunScanAsync(documentId, ScanOutcome.Unavailable);

        await scan.Should().ThrowAsync<VirusScannerUnavailableException>(
            "the job fails so the job server tries it again once the scanner is back");
        (await StateOfAsync(documentId)).Should().Be(
            DocumentState.PendingScan, "the first version marked it refused, as if a virus had been found");

        await using var scope = fixture.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await using var kept = await storage.OpenReadAsync(key, CancellationToken.None);
        (await new StreamReader(kept).ReadToEndAsync()).Should().Be(
            "%PDF-1.4 renewed licence", "the first version deleted the supplier's upload");
    }

    [Fact]
    public async Task The_same_document_scanned_clean_reaches_review()
    {
        var (documentId, _) = await PendingDocumentAsync();

        await RunScanAsync(documentId, ScanOutcome.Clean);

        (await StateOfAsync(documentId)).Should().Be(DocumentState.UnderReview);
    }
}
