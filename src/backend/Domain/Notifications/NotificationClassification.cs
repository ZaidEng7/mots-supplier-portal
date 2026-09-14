// Which notifications a user may switch off, and which always arrive.
//
// The distinction is what the message is for. An informational notification tells somebody what
// happened. An actionable one tells them what they must do. A user may opt out of the first kind
// and not the second, because a missed actionable message cannot be recovered: a tender they were
// invited to and did not bid on, a question they were asked and did not answer, an award they were
// given and did not accept in time.
//
// This is a list in one place rather than a flag on each send, because the classification is the
// decision, and spreading it across thirty-two call sites is how half of them end up disagreeing
// with it. It sits beside NotificationTypes because that is the list it has to stay exhaustive
// against, and a test fails if a type is added without being classified.
//
// It fails closed. IsMuteable answers false for anything it does not recognise, so an
// unclassified type is delivered rather than silently dropped. Over-delivering annoys somebody;
// under-delivering means a supplier is never told they won a contract.
//
// Four families are never muteable: invitations, clarification requests, award outcomes and
// document expiry. Three of them appear below. Document expiry does not, because it is an email
// reminder driven by its own job with no notification type behind it, so there is nothing here to
// mute and nothing here that could mute it. Written down rather than quietly satisfied: if that
// path ever gains a notification type, it has to be actionable.
//
//
// THE ACTIONABLE LIST, and why each entry is on it
//
// Buyer-side tender work, where each message is a queue item for a named person:
//
//   RfqSubmittedForReview   a manager has a tender waiting on their review
//   RfqReturnedForEdits     the officer has corrections to make
//   RfqApproved             approved and not published is a window nobody opened
//   RfqReassigned           this tender is now yours, and was not before
//
// The invitation family. There is no "you have been invited" type: an invitation is recorded while
// the tender is still a draft, and what reaches the supplier is the tender opening. That message
// is the invitation as far as a bidder is concerned, and it is the one whose non-arrival costs
// them the tender.
//
//   RfqSubmissionOpened
//
// The clarification family, both directions of an unanswered question:
//
//   RfqClarificationRequested
//   ProposalClarificationRequested
//   ProposalRevised         the answer arrives, and the committee has to read it
//
// Deadlines, where the window a bidder must act inside has moved:
//
//   RfqDeadlineExtended
//   RfqDeadlineShortened    the more urgent direction of the two
//
// Evaluation, where work has been assigned to a committee member:
//
//   EvaluationOpened
//   EvaluationReopened
//   EvaluationConsolidated  scores are in, and somebody has to finalise them
//   EvaluatorRecused        an evaluator has left, and the chair has a gap to fill
//
// The award-outcome family:
//
//   AwardRecommended
//   AwardRoutedForApproval
//   AwardApproved
//   AwardRejected
//   AwardReRecommended
//   ProposalAwardOffered    the supplier's offer, which expires if unanswered
//   ProposalDeclined        procurement moving on to the next bidder
//
// Operations:
//
//   AwardErpFailed          a failed sync somebody has to clear
//
// Lifecycle:
//
//   SupplierReinstated      actionable, and not obviously so. It reads like good news, but its
//                           non-arrival is the expensive kind: a supplier who believes they are
//                           still suspended does not bid, and the tender they skipped does not
//                           come back. Same argument as an invitation.
//
// Ambiguous cases were resolved towards actionable on purpose. Asking "did the recipient have to
// do something?" a few times too often produces a full inbox; getting it wrong the other way
// produces the four losses above. The full inbox is the recoverable mistake.
//
//
// THE INFORMATIONAL LIST, which a preference may switch off
//
// Every entry is here for the same reason: the fact it carries is recorded somewhere the recipient
// can see it, on the tender, the comparison screen or their own bid, so a user who muted it lost a
// convenience rather than an opportunity.
//
//   RfqSubmissionClosed         the window shut on time, and the tender says so
//   RfqClarificationResolved    the answer is published on the tender and stays there
//   RfqShortlistingStarted
//   RfqRecommendationRecorded
//   EvaluatorSubmitted          another evaluator's progress, not the recipient's
//   EvaluationFinalized         the result; the message that demands something is the award
//   AwardErpSynced              a success confirmation
//   ProposalWithdrawn           the bid pool changed, and every screen showing it already agrees
//   ProposalLapsed              the window closed on a draft, a loss with nothing left to do
//   ProposalCancelled           the tender was withdrawn, so the bid is over either way

namespace MotsSupplierPortal.Domain.Notifications;

public static class NotificationClassification
{
    public static readonly IReadOnlySet<string> Actionable = new HashSet<string>(StringComparer.Ordinal)
    {
        NotificationTypes.RfqSubmittedForReview,
        NotificationTypes.RfqReturnedForEdits,
        NotificationTypes.RfqApproved,
        NotificationTypes.RfqReassigned,

        NotificationTypes.RfqSubmissionOpened,

        NotificationTypes.RfqClarificationRequested,
        NotificationTypes.ProposalClarificationRequested,
        NotificationTypes.ProposalRevised,

        NotificationTypes.RfqDeadlineExtended,
        NotificationTypes.RfqDeadlineShortened,

        NotificationTypes.EvaluationOpened,
        NotificationTypes.EvaluationReopened,
        NotificationTypes.EvaluationConsolidated,
        NotificationTypes.EvaluatorRecused,

        NotificationTypes.AwardRecommended,
        NotificationTypes.AwardRoutedForApproval,
        NotificationTypes.AwardApproved,
        NotificationTypes.AwardRejected,
        NotificationTypes.AwardReRecommended,
        NotificationTypes.ProposalAwardOffered,
        NotificationTypes.ProposalDeclined,

        NotificationTypes.AwardErpFailed,

        NotificationTypes.SupplierReinstated,
    };

    public static readonly IReadOnlySet<string> Informational = new HashSet<string>(StringComparer.Ordinal)
    {
        NotificationTypes.RfqSubmissionClosed,
        NotificationTypes.RfqClarificationResolved,
        NotificationTypes.RfqShortlistingStarted,
        NotificationTypes.RfqRecommendationRecorded,
        NotificationTypes.EvaluatorSubmitted,
        NotificationTypes.EvaluationFinalized,
        NotificationTypes.AwardErpSynced,
        NotificationTypes.ProposalWithdrawn,
        NotificationTypes.ProposalLapsed,
        NotificationTypes.ProposalCancelled,
    };

    public static bool IsMuteable(string notificationType) => Informational.Contains(notificationType);

    public static bool IsActionable(string notificationType) => !IsMuteable(notificationType);
}
