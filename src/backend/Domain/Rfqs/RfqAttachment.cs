using MotsSupplierPortal.Domain.Common;
namespace MotsSupplierPortal.Domain.Rfqs;

/// <summary>An RFQ-level specification/document (DOMAIN-MODEL.md §5.4), linked to the shared
/// Document abstraction via IFileStorage - same pattern as SupplierDocument/ProposalDocument.</summary>
public sealed class RfqAttachment
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public string StorageKey { get; init; } = null!;
    public string OriginalFileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public string? Caption { get; set; }
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
