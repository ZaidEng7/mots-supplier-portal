// Every notification this system can produce.
//
// The list is transcribed from the written transition tables for tenders, bids, evaluation and
// awards rather than inferred from the code: each table has a notifications column naming both the
// event and who receives it.
//
// They are constants rather than an enum because the value is stored as text and read by the
// wording catalogue. Renaming one therefore has to be a visible change to a string that already
// exists in rows, not a silent renumbering.
//
// Most entries need no explanation beyond their name. These do.
//
// RfqReassigned says "this tender is now yours". It is not in the written table, because ownership
// did not exist to change when that table was written, so there was no transition to transcribe.
//
// RfqClarificationRequested, RfqClarificationResolved, RfqShortlistingStarted and
// RfqRecommendationRecorded arrived together, when three tender states became reachable and the
// table named a notification for each transition into and out of them.
//
// ProposalLapsed is a draft bid the submission window closed on. It goes to the supplier only,
// because nobody else needs to know and the supplier is the only party who lost something.
//
// ProposalCancelled is a bid ended by the tender being cancelled. It is a separate message from the
// tender-cancelled one on purpose: "the tender was withdrawn" and "your bid is closed" are
// different facts, and only the second is about the supplier's own work.
//
// RfqDeadlineShortened has no written rule behind it. The rules name a notification only for
// extending a deadline, but a window that closes earlier is the change a bidder most urgently needs
// to hear, because a supplier planning to submit on the old date otherwise discovers the new one by
// being refused. Both directions are sent; the extra notification is the addition, not the
// omission.
//
// SupplierReinstated is participation restored after an automatic suspension, because the
// replacement document was approved. It is not in the transition tables either, because the
// suspension it reverses is not described there.
//
// All is the full set, and both directions of the catalogue check compare against it: no type
// without wording, and no wording without a type.

namespace MotsSupplierPortal.Domain.Notifications;

public static class NotificationTypes
{
    public const string RfqSubmittedForReview = "rfq.submitted_for_review";
    public const string RfqReturnedForEdits = "rfq.returned_for_edits";
    public const string RfqApproved = "rfq.approved";
    public const string RfqReassigned = "rfq.reassigned";
    public const string RfqSubmissionOpened = "rfq.submission_opened";
    public const string RfqSubmissionClosed = "rfq.submission_closed";
    public const string RfqClarificationRequested = "rfq.clarification_requested";
    public const string RfqClarificationResolved = "rfq.clarification_resolved";
    public const string RfqShortlistingStarted = "rfq.shortlisting_started";
    public const string RfqRecommendationRecorded = "rfq.recommendation_recorded";
    public const string RfqDeadlineExtended = "rfq.deadline_extended";
    public const string RfqDeadlineShortened = "rfq.deadline_shortened";

    public const string EvaluationOpened = "evaluation.opened";
    public const string EvaluatorSubmitted = "evaluation.evaluator_submitted";
    public const string EvaluationConsolidated = "evaluation.consolidated";
    public const string EvaluationFinalized = "evaluation.finalized";
    public const string EvaluationReopened = "evaluation.reopened";
    public const string EvaluatorRecused = "evaluation.evaluator_recused";

    public const string AwardRecommended = "award.recommended";
    public const string AwardRoutedForApproval = "award.routed_for_approval";
    public const string AwardApproved = "award.approved";
    public const string AwardRejected = "award.rejected";
    public const string AwardReRecommended = "award.re_recommended";
    public const string AwardErpSynced = "award.erp_synced";
    public const string AwardErpFailed = "award.erp_failed";

    public const string ProposalWithdrawn = "proposal.withdrawn";
    public const string ProposalAwardOffered = "proposal.award_offered";
    public const string ProposalDeclined = "proposal.declined";
    public const string ProposalLapsed = "proposal.lapsed";
    public const string ProposalCancelled = "proposal.cancelled";
    public const string ProposalClarificationRequested = "proposal.clarification_requested";
    public const string ProposalRevised = "proposal.revised";

    public const string SupplierReinstated = "supplier.reinstated";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        RfqSubmittedForReview, RfqReturnedForEdits, RfqApproved, RfqReassigned, RfqSubmissionOpened, RfqSubmissionClosed,
        RfqClarificationRequested, RfqClarificationResolved, RfqShortlistingStarted, RfqRecommendationRecorded,
        EvaluationOpened, EvaluatorSubmitted, EvaluationConsolidated, EvaluationFinalized,
        EvaluationReopened, EvaluatorRecused,
        AwardRecommended, AwardRoutedForApproval, AwardApproved, AwardRejected, AwardReRecommended,
        AwardErpSynced, AwardErpFailed,
        ProposalWithdrawn,
        ProposalAwardOffered,
        ProposalDeclined,
        ProposalLapsed,
        ProposalCancelled,
        RfqDeadlineExtended,
        RfqDeadlineShortened,
        ProposalClarificationRequested,
        ProposalRevised,
        SupplierReinstated,
    };
}
