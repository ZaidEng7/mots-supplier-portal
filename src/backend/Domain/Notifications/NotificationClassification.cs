namespace MotsSupplierPortal.Domain.Notifications;

/// <summary>
/// D-60/FR-NOT-004: which notifications a user may switch off, and which always arrive.
///
/// <para><b>The distinction, in D-60's own words:</b> an informational notification tells someone what
/// happened; an actionable one tells them what they must do. A user may opt out of the first kind and not
/// the second, because the cost of a missed actionable message is not recoverable - a tender they were
/// invited to and did not bid on, a question they were asked and did not answer, an award they were given
/// and did not accept in time.</para>
///
/// <para><b>Why this is a table and not a flag on the send path.</b> The classification is the decision;
/// scattering it across thirty-two call sites is how half of them end up disagreeing with it. It lives
/// beside <see cref="NotificationTypes"/> because that is the list it has to stay exhaustive against, and
/// <c>NotificationClassificationTests</c> fails if a type is added without being classified.</para>
///
/// <para><b>Fail-closed.</b> <see cref="IsMuteable"/> answers false for anything it does not recognise. An
/// unclassified type is delivered rather than silently suppressed: the failure of over-delivery is an
/// irritated user, and the failure of under-delivery is somebody not being told they had been awarded a
/// contract.</para>
///
/// <para><b>The four families D-60 names as never muteable</b> are invitations, clarification requests,
/// award outcomes and document expiry. Three of them are notification types below. <b>Document expiry is
/// not</b> - it is an email reminder driven by <c>DocumentExpiryReminder</c> and <c>DocumentExpiryJob</c>,
/// with no member of <see cref="NotificationTypes"/> behind it, so there is nothing here to mute and
/// nothing here that could mute it. Recorded rather than quietly satisfied: if that path ever gains a
/// notification type, D-60 requires it to be actionable.</para>
/// </summary>
public static class NotificationClassification
{
    /// <summary>
    /// The types that tell somebody what they must do. Every one of these is delivered regardless of
    /// preference.
    ///
    /// <para>Ambiguous cases were resolved towards actionable deliberately. Being asked "did the recipient
    /// have to do something?" a few times too often produces an over-full inbox; getting it wrong the other
    /// way produces the four losses D-60 lists. The over-full inbox is the recoverable mistake.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> Actionable = new HashSet<string>(StringComparer.Ordinal)
    {
        // ---- Buyer-side tender work: each of these is a queue item for a named person ----
        NotificationTypes.RfqSubmittedForReview,    // a manager has a tender waiting on their review
        NotificationTypes.RfqReturnedForEdits,      // the officer has corrections to make
        NotificationTypes.RfqApproved,              // approved and not published is a window nobody opened
        NotificationTypes.RfqReassigned,            // A-7: this tender is now yours, and was not before

        // ---- The invitation family (D-60) ----
        //
        // There is no "you have been invited" type: an invitation is recorded while the tender is still in
        // Draft, and what reaches the supplier is the tender OPENING. That message is the invitation as far
        // as a bidder is concerned, and it is the one whose non-arrival costs them the tender.
        NotificationTypes.RfqSubmissionOpened,

        // ---- The clarification family (D-60): both directions of an unanswered question ----
        NotificationTypes.RfqClarificationRequested,
        NotificationTypes.ProposalClarificationRequested,
        NotificationTypes.ProposalRevised,          // the answer arrives; the committee has to read it

        // ---- Deadlines: the window a bidder must act inside has moved ----
        NotificationTypes.RfqDeadlineExtended,
        NotificationTypes.RfqDeadlineShortened,     // the more urgent direction - see its own doc comment

        // ---- Evaluation: work assigned to a committee member ----
        NotificationTypes.EvaluationOpened,
        NotificationTypes.EvaluationReopened,
        NotificationTypes.EvaluationConsolidated,   // scores are in; somebody has to finalise them
        NotificationTypes.EvaluatorRecused,         // an evaluator has left; the chair has a gap to fill

        // ---- The award-outcome family (D-60) ----
        NotificationTypes.AwardRecommended,
        NotificationTypes.AwardRoutedForApproval,
        NotificationTypes.AwardApproved,
        NotificationTypes.AwardRejected,
        NotificationTypes.AwardReRecommended,
        NotificationTypes.ProposalAwardOffered,     // the supplier's offer, which expires if unanswered
        NotificationTypes.ProposalDeclined,         // procurement's move to the next bidder

        // ---- Operations ----
        NotificationTypes.AwardErpFailed,           // a failed sync somebody has to clear
    };

    /// <summary>
    /// The types that tell somebody what happened. These are what a preference may switch off.
    ///
    /// <para>Each one is here for the same reason: the fact it carries is durable somewhere the recipient
    /// can see it - on the tender, the comparison screen or the supplier's own bid - so a user who muted it
    /// has lost a convenience rather than an opportunity.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> Informational = new HashSet<string>(StringComparer.Ordinal)
    {
        NotificationTypes.RfqSubmissionClosed,       // the window shut on time; the tender says so
        NotificationTypes.RfqClarificationResolved,  // the answer is published on the tender and stays there
        NotificationTypes.RfqShortlistingStarted,
        NotificationTypes.RfqRecommendationRecorded,
        NotificationTypes.EvaluatorSubmitted,        // another evaluator's progress, not the recipient's
        NotificationTypes.EvaluationFinalized,       // the result; the message that DEMANDS something is the award
        NotificationTypes.AwardErpSynced,            // a success confirmation
        NotificationTypes.ProposalWithdrawn,         // the bid pool changed, and every screen showing it already agrees
        NotificationTypes.ProposalLapsed,            // A-9: the window closed on a draft - a loss with nothing left to do
        NotificationTypes.ProposalCancelled,         // the tender was withdrawn; the bid is over either way
    };

    /// <summary>Whether a user may switch this notification off. False for anything unrecognised.</summary>
    public static bool IsMuteable(string notificationType) => Informational.Contains(notificationType);

    /// <summary>Whether this notification arrives regardless of preference.</summary>
    public static bool IsActionable(string notificationType) => !IsMuteable(notificationType);
}
