// Where one uploaded supplier document is in its life.
//
//   PendingScan    uploaded, waiting on the virus scanner
//   ScanRejected   the scanner refused it
//   Uploaded       clean, waiting for a reviewer
//   UnderReview    a reviewer has it
//   Approved       accepted
//   Rejected       refused by the reviewer
//   ExpiringSoon   approved, and its expiry date is close
//   Expired        approved, and its expiry date has passed
//
// The last two are reached by the passage of time rather than by anybody acting.
//
// A row exists only once a file has actually been uploaded. "Required but not yet uploaded" is worked
// out for display, from a required document type with no matching row, and is not a state stored here.

namespace MotsSupplierPortal.Domain.Suppliers;

public enum DocumentState
{
    PendingScan,
    ScanRejected,
    Uploaded,
    UnderReview,
    Approved,
    Rejected,
    ExpiringSoon,
    Expired,
}
