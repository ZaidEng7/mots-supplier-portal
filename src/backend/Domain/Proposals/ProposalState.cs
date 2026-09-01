namespace MotsSupplierPortal.Domain.Proposals;

/// <summary>Canonical machine (BUSINESS-PROCESSES.md §4): "Draft -&gt; Submitted -&gt; UnderReview
/// -&gt; (ClarificationRequested -&gt; Revised -&gt; UnderReview)* -&gt; Shortlisted | NotSelected ->
/// AwardOffered -&gt; Awarded | Declined"; supplier-initiated Withdrawn from Draft or Submitted
/// while SubmissionOpen.
///
/// <para><b>EPIC-09 scope:</b> only Draft, Submitted, and Withdrawn have real domain transitions
/// this build. UnderEvaluation onward is FEAT-09.7 (evaluation-intake/outcome transitions),
/// explicitly left as an enum-only stub pending EPIC-11 (evaluation opens) and EPIC-14 (award) -
/// same "real values, no transition method yet" pattern as RfqState's own UnderEvaluation-onward
/// values (see Rfq.cs's own doc comment).</para></summary>
public enum ProposalState
{
    Draft,
    Submitted,
    Withdrawn,
    UnderReview,
    ClarificationRequested,
    Revised,
    Shortlisted,
    NotSelected,
    AwardOffered,
    Awarded,
    Declined,
}
