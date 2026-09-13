// A specification or document attached to a tender. Stored through the same file storage that supplier
// documents and bid documents use.
//
// ScanState starts as pending, so a row is never servable until the virus scanner has actually looked
// at the file.

namespace MotsSupplierPortal.Domain.Rfqs;

using MotsSupplierPortal.Domain.Common;

public sealed class RfqAttachment
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public string StorageKey { get; init; } = null!;
    public string OriginalFileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public string? Caption { get; set; }
    public DateTimeOffset UploadedAt { get; init; }

    public AttachmentScanState ScanState { get; private set; } = AttachmentScanState.PendingScan;

    public void MarkScanClean() => ScanState = AttachmentScanState.Clean;

    public void MarkScanRejected() => ScanState = AttachmentScanState.ScanRejected;
}
