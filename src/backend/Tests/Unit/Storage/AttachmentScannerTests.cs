// What happens to a tender attachment or bid document when it is scanned on first download.
//
// THE OUTAGE CASE IS THE ONE THAT MATTERS. The scanner used to report "could not answer" as infected, and this path
// deletes an infected object, so whoever first opened a tender specification during a scanner outage deleted it for
// everyone. The test asserts the object is still in storage and was neither marked rejected nor clean.
//
// THE CONTROLS ARE THE OTHER TWO VERDICTS: an infected file is still deleted and marked, and a clean one is still
// marked clean. Without them the outage test would pass against a scanner that never deleted or marked anything.

namespace MotsSupplierPortal.Tests.Unit.Storage;

using FluentAssertions;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed class AttachmentScannerTests
{
    private sealed class FixedScanner(ScanOutcome outcome) : IVirusScanner
    {
        public Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct) => Task.FromResult(outcome);
    }

    private sealed class MemoryStorage : IFileStorage
    {
        public HashSet<string> Keys { get; } = ["rfqs/RFQ-2026-000001/specification.pdf"];

        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
        {
            Keys.Add(key);
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));

        public Task MoveAsync(string sourceKey, string destinationKey, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Keys.Remove(key);
            return Task.CompletedTask;
        }

        public Task<string> GetSignedDownloadUrlAsync(
            string key, TimeSpan expiry, string downloadFileName, CancellationToken ct) =>
            Task.FromResult("https://storage.example/" + key);
    }

    private const string Key = "rfqs/RFQ-2026-000001/specification.pdf";

    private static async Task<(AttachmentScanVerdict Verdict, bool Clean, bool Rejected, MemoryStorage Storage)> ScanAsync(
        ScanOutcome outcome)
    {
        var storage = new MemoryStorage();
        var clean = false;
        var rejected = false;

        var verdict = await new AttachmentScanner(storage, new FixedScanner(outcome)).EnsureScannedAsync(
            AttachmentScanState.PendingScan, Key, () => clean = true, () => rejected = true, CancellationToken.None);

        return (verdict, clean, rejected, storage);
    }

    [Fact]
    public async Task A_scanner_outage_keeps_the_file_and_marks_nothing()
    {
        var (verdict, clean, rejected, storage) = await ScanAsync(ScanOutcome.Unavailable);

        verdict.Should().Be(AttachmentScanVerdict.ScannerUnavailable);
        storage.Keys.Should().Contain(
            Key, "whoever opened the specification during an outage used to delete it for every bidder");
        rejected.Should().BeFalse();
        clean.Should().BeFalse("nothing has said it is clean, so it must not be served");
    }

    [Fact]
    public async Task An_infected_file_is_still_deleted_and_marked()
    {
        var (verdict, _, rejected, storage) = await ScanAsync(ScanOutcome.Infected);

        verdict.Should().Be(AttachmentScanVerdict.Rejected);
        rejected.Should().BeTrue();
        storage.Keys.Should().NotContain(Key);
    }

    [Fact]
    public async Task A_clean_file_is_still_marked_clean_and_served()
    {
        var (verdict, clean, _, storage) = await ScanAsync(ScanOutcome.Clean);

        verdict.Should().Be(AttachmentScanVerdict.Safe);
        clean.Should().BeTrue();
        storage.Keys.Should().Contain(Key);
    }
}
