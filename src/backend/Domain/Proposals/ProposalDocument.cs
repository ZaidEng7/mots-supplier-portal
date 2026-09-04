using MotsSupplierPortal.Domain.Common;
namespace MotsSupplierPortal.Domain.Proposals;

/// <summary>FEAT-09.3/FR-PRP-004: supporting files for a proposal. Stored via the existing
/// IFileStorage, same pattern as RfqAttachment/SupplierDocument - no new storage mechanism invented.
///
/// <para><b>Corrected in batch 8.</b> This comment previously asserted these files were
/// "two-envelope TECHNICAL content (never pricing)". That was an assumption about supplier
/// behaviour dressed as a property of the system: nothing read the bytes, nothing constrained them,
/// and the claim was load-bearing for a buyer-side read that did not exist yet. It is now an
/// explicit per-file <see cref="ProposalDocumentEnvelope"/> that defaults to Commercial.</para></summary>
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
    /// T-028 / D-7: which envelope this file belongs to. Commercial unless the uploader said
    /// otherwise - see ProposalDocumentEnvelope for why the default leans that way.
    /// </summary>
    public ProposalDocumentEnvelope Envelope { get; init; } = ProposalDocumentEnvelope.Commercial;

    /// <summary>
    /// D-10: the AV scan gate. Defaults to PendingScan, so a row is never servable until something
    /// has actually looked at it - see AttachmentScanState for why this is a default rather than an
    /// answer, and why it is not DocumentState.
    /// </summary>
    public AttachmentScanState ScanState { get; private set; } = AttachmentScanState.PendingScan;

    public void MarkScanClean() => ScanState = AttachmentScanState.Clean;

    public void MarkScanRejected() => ScanState = AttachmentScanState.ScanRejected;

}
