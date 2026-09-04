namespace MotsSupplierPortal.Domain.Common;

/// <summary>
/// The AV scan dimension for RFQ attachments and proposal documents.
///
/// <para><b>D-10, and this is a DEFAULT rather than an answer.</b> OQ-014 still carries
/// <c>[REQUIRES BUSINESS CONFIRMATION]</c> on AV scanning generally. The decision recorded as D-10 -
/// scan everything, fail closed - is the conservative reading taken so the gap stops being silent,
/// on the principle that a default should under-serve rather than over-disclose. A business answer
/// should change VALUES here (which states are servable, whether a scan is required at all), not the
/// design: the state field, the gate and the scan job all stay.</para>
///
/// <para><b>Deliberately not <c>DocumentState</c>.</b> That enum is the supplier-document LIFECYCLE -
/// Uploaded, UnderReview, Approved, Rejected, ExpiringSoon, Expired - and an RFQ attachment has none
/// of those. Reusing it would give both aggregates six states they can never occupy and one they
/// need. This is the scan dimension only.</para>
/// </summary>
public enum AttachmentScanState
{
    /// <summary>
    /// Uploaded and not yet scanned. The DEFAULT for new rows and the backfill value for existing
    /// ones - D-10 is explicit that rows predating the scan are not assumed clean, because "we never
    /// looked" and "we looked and it was fine" are different facts and only one of them is safe.
    /// </summary>
    PendingScan,

    /// <summary>Scanned and clean. The only state a download is served from.</summary>
    Clean,

    /// <summary>The scanner found something. The object is deleted; the row is kept as the audit
    /// trail, matching how SupplierDocument.ScanRejected already behaves.</summary>
    ScanRejected,
}
