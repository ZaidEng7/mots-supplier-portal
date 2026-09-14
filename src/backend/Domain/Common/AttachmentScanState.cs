// Whether an uploaded file has been checked for viruses yet. Applies to tender
// attachments and to proposal documents.
//
//   PendingScan    uploaded, not yet checked. The default for new rows, and the value
//                  given to rows that existed before scanning was added: "we never
//                  looked" and "we looked and it was fine" are different facts, and only
//                  one of them is safe to act on.
//   Clean          checked and safe. The only state a download is served from.
//   ScanRejected   the scanner found something. The file is deleted and the row is kept
//                  as the audit trail, which is how a rejected supplier document already
//                  behaves.
//
// This is deliberately not DocumentState, which is the supplier-document lifecycle:
// Uploaded, UnderReview, Approved, Rejected, ExpiringSoon, Expired. A tender attachment
// has none of those. Reusing that enum would give both kinds of file six states they can
// never occupy and miss the one they need.
//
// Scanning everything and refusing to serve anything unscanned is a conservative default
// rather than a ministry decision, taken so the gap stops being silent. A business answer
// should change which states are servable, or whether a scan is required at all. It
// should not change this design: the state, the download gate and the scan job all stay.

namespace MotsSupplierPortal.Domain.Common;

public enum AttachmentScanState
{
    PendingScan,
    Clean,
    ScanRejected,
}
