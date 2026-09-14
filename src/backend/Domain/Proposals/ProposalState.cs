// Where a bid is in its life, and the three groups of states that queries ask about.
//
// The lifecycle:
//
//   Draft                    the supplier is still working on it
//   Submitted                sealed and handed in
//   UnderReview              the committee has it
//   ClarificationRequested   the committee asked the supplier something
//   Revised                  the supplier answered, and it goes back under review
//   Shortlisted              still in contention
//   NotSelected              not the winner
//   AwardOffered             the winner has been offered the contract
//   Awarded                  the supplier accepted
//   Declined                 the supplier said no
//
// A supplier may withdraw from Draft or Submitted while the window is open, which reaches Withdrawn.
//
// Two terminal states end a bid without anybody deciding anything about it.
//
// Lapsed is a draft the submission window closed on. It is deliberately not the same state as
// Cancelled: "you ran out of time" and "the tender was withdrawn" are different events, and a
// supplier reading their list of bids has to be able to tell them apart. Collapsing them would make
// the product say the wrong one half the time.
//
// Cancelled is a live bid whose tender was cancelled underneath it.
//
//
// THE THREE GROUPS
//
// InEvaluation is every state a bid occupies while it is in the evaluation set: submitted and not yet
// resolved to an outcome.
//
// It exists because the middle of the lifecycle used to be unreachable, so every live bid sat in
// Submitted and six separate queries filtered on that one value and were correct by accident. Once
// intake began moving bids to UnderReview, each of those queries would silently have returned
// nothing: the evaluator's workspace empty, the comparison matrix empty, and no error anywhere. One
// definition means widening the lifecycle again cannot leave five of six places behind.
//
// UnderComparison is every bid a comparison should carry: the evaluation set, the one that has been
// offered the award, and the three outcomes an offer resolves to.
//
// It is its own group rather than a wider InEvaluation for two reasons. Executing an award freezes
// the comparison into the permanent award record, and approving moves the winner to AwardOffered,
// which would drop the winner out of the evaluation set and leave the winning bid missing from the
// award's own file. Widening the evaluation set would have fixed that and simultaneously put an
// offered award back into the evaluator's workspace, which is a different question with a different
// answer.
//
// The outcomes belong in this group too, and their absence used to empty the screen: executing an
// award moves the winner to Awarded and every other live bid to NotSelected, so a buyer who opened
// the comparison a minute after the award saw the tender title, the item columns, and no bids
// underneath them. The frozen snapshot was never affected, which is exactly why this stayed
// invisible: the snapshot is taken before the transition and a test asserts it, while nothing
// asserted the live screen afterwards.
//
// Declined is in the group for the same reason: a supplier declining the offer sends the tender back
// to recommendation, and the buyer picking an alternate is reading this very screen to do it. The
// states that stay out are the ones that were never in a comparison: Draft, Withdrawn, Lapsed and
// Cancelled.
//
// Resolved is what a supplier means by "the result". AwardOffered is in it although it is not final,
// because an offer is a result the supplier has to answer, and leaving it out would make the
// dashboard go quiet at the one moment it has something to say. Withdrawn, Lapsed and Cancelled are
// not results in this sense, since nobody decided anything, and each already has its own place on the
// supplier's screen.
//
// All three are arrays rather than sets. The database layer can translate a membership test over an
// array into a SQL IN clause and cannot do so over a set, which failed at run time with a server
// error rather than at compile time. Set semantics were never the point here; the membership test is.

namespace MotsSupplierPortal.Domain.Proposals;

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
    Lapsed,
    Cancelled,
}

public static class ProposalStates
{
    public static readonly ProposalState[] InEvaluation =
    {
        ProposalState.Submitted,
        ProposalState.UnderReview,
        ProposalState.ClarificationRequested,
        ProposalState.Revised,
        ProposalState.Shortlisted,
    };

    public static readonly ProposalState[] Resolved =
    {
        ProposalState.AwardOffered,
        ProposalState.Awarded,
        ProposalState.NotSelected,
        ProposalState.Declined,
    };

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
