// A supporting file attached to a bid.
//
// Stored through the same file storage that tender attachments and supplier documents use, so no new
// storage mechanism was invented for this.
//
// Envelope says which of the two envelopes the file belongs to, and it defaults to commercial.
//
// An earlier version of this file claimed these documents were always technical content and never
// pricing. That was an assumption about how suppliers behave dressed up as a property of the system:
// nothing read the bytes, nothing constrained them, and the claim was about to be relied on by a
// buyer-side read. It is now a stored answer per file instead.
//
// ScanState starts as pending, so a row is never servable until the virus scanner has actually
// looked at the file.

namespace MotsSupplierPortal.Domain.Proposals;

using MotsSupplierPortal.Domain.Common;

public sealed class ProposalDocument
{
    public Guid Id { get; init; }
    public Guid ProposalId { get; init; }
    public string StorageKey { get; init; } = null!;
    public string OriginalFileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public string? Caption { get; init; }
    public DateTimeOffset UploadedAt { get; init; }

    public ProposalDocumentEnvelope Envelope { get; init; } = ProposalDocumentEnvelope.Commercial;

    public AttachmentScanState ScanState { get; private set; } = AttachmentScanState.PendingScan;

    public void MarkScanClean() => ScanState = AttachmentScanState.Clean;

    public void MarkScanRejected() => ScanState = AttachmentScanState.ScanRejected;
}
