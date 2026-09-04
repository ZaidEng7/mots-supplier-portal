using MotsSupplierPortal.Domain.Common;
namespace MotsSupplierPortal.Domain.Proposals;

/// <summary>FEAT-09.3/FR-PRP-004: compliance/technical supporting files - part of the two-envelope
/// TECHNICAL content (never pricing). Stored via the existing IFileStorage, same pattern as
/// RfqAttachment/SupplierDocument - no new storage mechanism invented.</summary>
public sealed class ProposalDocument
{
    public Guid Id { get; init; }
    public Guid ProposalId { get; init; }
    public string StorageKey { get; init; } = null!;
    public string OriginalFileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public string? Caption { get; init; }
    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>
    /// D-10: the AV scan gate. Defaults to PendingScan, so a row is never servable until something
    /// has actually looked at it - see AttachmentScanState for why this is a default rather than an
    /// answer, and why it is not DocumentState.
    /// </summary>
    public AttachmentScanState ScanState { get; private set; } = AttachmentScanState.PendingScan;

    public void MarkScanClean() => ScanState = AttachmentScanState.Clean;

    public void MarkScanRejected() => ScanState = AttachmentScanState.ScanRejected;

}
