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

    /// <summary>
    /// A-9/BRULE-052: a Draft the submission window closed on. Terminal.
    ///
    /// <para>Distinct from <see cref="Cancelled"/> deliberately. "You ran out of time" and "the tender
    /// was withdrawn" are different events, and a supplier reading their proposal list has to be able
    /// to tell them apart - collapsing them into one state would make the product say the wrong one
    /// half the time.</para>
    /// </summary>
    Lapsed,

    /// <summary>A-9/BRULE-056: a live proposal whose RFQ was cancelled beneath it. Terminal. BRULE-056
    /// carries no assumption tag, so its previous half-enforcement - notify everyone, move nothing -
    /// was a confirmed rule going unenforced.</summary>
    Cancelled,
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
    /// T-064, then T-070: every proposal a COMPARISON should carry - the evaluation set, the one
    /// that has been offered the award, and the three outcomes an offer resolves to.
    ///
    /// <para><b>Why this is its own set rather than a widening of InEvaluation.</b>
    /// ExecuteAwardHandler snapshots the comparison into the permanent award record, and the
    /// comparison filtered on InEvaluation. Once approve moves the winner to AwardOffered, the winner
    /// falls out of that set - so the award's own snapshot would have omitted the winning bid.
    /// Widening InEvaluation would have fixed that and simultaneously put an offered award back into
    /// the evaluator's workspace, which is a different question with a different answer.</para>
    ///
    /// <para><b>T-070: the outcomes belong here too, and their absence emptied the screen.</b>
    /// Executing an award moves the winner to <c>Awarded</c> and every other live bid to
    /// <c>NotSelected</c> - both outside the set as it stood - so a buyer who opened the comparison
    /// one minute after the award saw the RFQ title, the item columns, and no bids under them. The
    /// award's own frozen snapshot was never affected, which is exactly why this stayed invisible:
    /// the snapshot is taken BEFORE the transition and a test asserts it, while nothing asserted the
    /// live view AFTER it.</para>
    ///
    /// <para><c>Declined</c> is here for the same reason as the other two: a supplier declining the
    /// offer returns the RFQ to Recommendation, and the buyer choosing an alternate is reading this
    /// very screen to do it. The states that stay OUT are the ones that were never in a comparison -
    /// <c>Draft</c>, <c>Withdrawn</c>, <c>Lapsed</c> and <c>Cancelled</c>.</para>
    /// </summary>
    public static readonly ProposalState[] UnderComparison =
    {
        ProposalState.Submitted,
        ProposalState.UnderReview,
        ProposalState.ClarificationRequested,
        ProposalState.Revised,
        ProposalState.Shortlisted,
        ProposalState.AwardOffered,
        ProposalState.Awarded,
        ProposalState.NotSelected,
        ProposalState.Declined,
    };
}
