// Where a tender is in its life.
//
//   Draft             being written
//   InternalReview    waiting on a manager
//   Approved          signed off, not yet open to suppliers
//   Published         visible to the invited suppliers
//   SubmissionOpen    accepting bids
//   SubmissionClosed  the window has shut
//   UnderEvaluation   the committee is scoring
//   Clarification     a question has been put to a bidder mid-evaluation
//   Shortlisting      narrowing the field
//   Recommendation    a winner has been named
//   AwardApproval     waiting on the approver
//   Awarded           the award has been issued
//   Completed         the purchase order came back from the finance system
//
// Cancelled is reachable from any state before Awarded.
//
// The whole lifecycle is declared here rather than grown a value at a time, so the full shape of the
// process is visible in one place. The transitions themselves were built in stages, and a state with no
// method that reaches it is an explicit placeholder rather than an oversight.

namespace MotsSupplierPortal.Domain.Rfqs;

public enum RfqState
{
    Draft,
    InternalReview,
    Approved,
    Published,
    SubmissionOpen,
    SubmissionClosed,
    UnderEvaluation,
    Clarification,
    Shortlisting,
    Recommendation,
    AwardApproval,
    Awarded,
    Completed,
    Cancelled,
}
