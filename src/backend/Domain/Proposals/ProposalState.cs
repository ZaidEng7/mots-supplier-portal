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

/// <summary>
/// The states a proposal occupies while it is IN the evaluation set - submitted and not yet resolved
/// to an outcome.
///
/// <para>T-051 made this necessary. Before it, the middle of the lifecycle was unreachable and every
/// live proposal sat in <c>Submitted</c>, so six separate queries filtered on that one member and
/// were correct by accident. Once intake moves proposals to <c>UnderReview</c>, each of those
/// queries silently returns nothing - the evaluator's workspace empties, the comparison matrix
/// empties, and no error is raised anywhere.</para>
///
/// <para>One definition, so widening the lifecycle again cannot leave five of six call sites behind.
/// The set is §4.1's own: everything between submission and a terminal outcome.</para>
/// </summary>
public static class ProposalStates
{
    /// <summary>
    /// An ARRAY, not a HashSet. EF Core translates <c>Contains</c> over an array or list into SQL
    /// <c>IN (...)</c>; over an <c>IReadOnlySet</c> it cannot, and the query failed at runtime with a
    /// 500 rather than at compile time. The set semantics were never the point here - the membership
    /// test is.
    /// </summary>
    public static readonly ProposalState[] InEvaluation =
    {
        ProposalState.Submitted,
        ProposalState.UnderReview,
        ProposalState.ClarificationRequested,
        ProposalState.Revised,
        ProposalState.Shortlisted,
    };

    /// <summary>
    /// T-064: every proposal a COMPARISON should carry - the evaluation set plus the one that has
    /// been offered the award.
    ///
    /// <para><b>Why this is its own set rather than a widening of InEvaluation.</b>
    /// ExecuteAwardHandler snapshots the comparison into the permanent award record, and the
    /// comparison filtered on InEvaluation. Once approve moves the winner to AwardOffered, the winner
    /// falls out of that set - so the award's own snapshot would have omitted the winning bid.
    /// Widening InEvaluation would have fixed that and simultaneously put an offered award back into
    /// the evaluator's workspace, which is a different question with a different answer.</para>
    /// </summary>
    public static readonly ProposalState[] UnderComparison =
    {
        ProposalState.Submitted,
        ProposalState.UnderReview,
        ProposalState.ClarificationRequested,
        ProposalState.Revised,
        ProposalState.Shortlisted,
        ProposalState.AwardOffered,
    };
}
