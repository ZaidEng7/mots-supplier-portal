namespace MotsSupplierPortal.Domain.Rfqs;

/// <summary>Canonical RFQ lifecycle (docs/product/BUSINESS-PROCESSES.md §3, verified directly
/// against that document, not summarized): Draft -> InternalReview -> Approved -> Published ->
/// SubmissionOpen -> SubmissionClosed -> UnderEvaluation -> Clarification* -> Shortlisting ->
/// Recommendation -> AwardApproval -> Awarded -> Completed; Cancelled reachable from any
/// pre-Awarded state. Only the states this session's build (EPIC-07 authoring/publish/window/
/// cancel) actually drives are implemented as domain transitions below - UnderEvaluation onward
/// are placeholders reserved for EPIC-11/12/13/14 and are NOT reachable by any method on this
/// aggregate yet (FEAT-07.7, left as an explicit stub per this session's own scope).</summary>
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
