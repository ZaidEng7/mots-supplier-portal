namespace MotsSupplierPortal.Infrastructure.Storage;

/// <summary>
/// Builds the object-store key for an attachment, from server-side values only.
///
/// <para><b>T-026, the defect this closes.</b> Both upload paths built their key by interpolation -
/// <c>$"rfq-attachments/{referenceCode}/{guid}-{file.FileName}"</c> - and BOTH interpolated values
/// are caller input. The file name is the obvious one: a name containing <c>../</c> shapes the key.
/// The reference code is the one that is easy to miss, because it looks like it belongs to us: it is
/// a route parameter, and it is not validated against a real RFQ until the handler runs, several
/// lines after the bytes have already been written under that key.</para>
///
/// <para>So the key is a fixed prefix and a GUID this process generated, and nothing else. Flat
/// rather than grouped by reference code - the grouping was the only thing the interpolation bought,
/// and the row already records which parent the object belongs to.</para>
///
/// <para><b>The file name is not lost.</b> It is stored on the row and is what the download's
/// <c>Content-Disposition</c> reads (see <see cref="ContentDisposition"/>). It is metadata about the
/// object, never part of its address.</para>
///
/// <para><b>Existing objects are unaffected.</b> A download reads the key FROM the row, so rows
/// written under the old scheme keep resolving to their old keys. Nothing needs migrating; only new
/// uploads take the new shape.</para>
///
/// <para><b>T-025, stated here because both callers share it.</b> Neither of these paths is
/// quarantined, and supplier documents are. That asymmetry is deliberate as of this commit rather
/// than unnoticed. It is not blocked on the scanner - <c>ClamAvScanner</c> is a real clamd client,
/// registered and fail-closed. It is blocked because quarantine-first is a STATE MACHINE
/// (<c>DocumentState.PendingScan</c>/<c>ScanRejected</c> and the <c>MarkScanClean</c>/
/// <c>MarkScanRejected</c> transitions) and neither <c>RfqAttachment</c> nor <c>ProposalDocument</c>
/// has a state field at all - so there is nothing to gate a download on and nothing to say what the
/// rows already in the table are. That last question is OQ-014, tagged
/// [REQUIRES BUSINESS CONFIRMATION]. Guessing it means either quarantining files procurement expects
/// to publish at once, or adding a scan-state column that always reads Clean, which is worse than no
/// column because it looks like a control. See BACKLOG-REMEDIATION.md T-025.</para>
/// </summary>
public static class AttachmentStorageKey
{
    public const string RfqAttachmentPrefix = "rfq-attachments";
    public const string ProposalDocumentPrefix = "proposal-documents";

    public static string For(string prefix) => $"{prefix}/{Guid.CreateVersion7()}";
}
