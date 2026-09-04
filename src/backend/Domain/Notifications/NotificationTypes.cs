namespace MotsSupplierPortal.Domain.Notifications;

/// <summary>
/// Every notification this system can produce, transcribed from BUSINESS-PROCESSES.md's own
/// transition tables rather than inferred: the "Notifications" column of §3.1 (RFQ), §3.2 (Proposal),
/// §3.3 (Evaluation) and §3.4 (Award) names both the event and its recipients.
///
/// <para>Constants rather than an enum because the value is persisted as text (DATABASE-MODEL.md
/// §2.7's <c>type text</c>) and read by the copy catalogue, so renaming one must be a visible change
/// to a string that already exists in rows, not a silent renumbering.</para>
/// </summary>
public static class NotificationTypes
{
    // ---- RFQ lifecycle (BUSINESS-PROCESSES §3.1) ----
    public const string RfqSubmittedForReview = "rfq.submitted_for_review";
    public const string RfqReturnedForEdits = "rfq.returned_for_edits";
    public const string RfqApproved = "rfq.approved";
    public const string RfqSubmissionOpened = "rfq.submission_opened";
    public const string RfqSubmissionClosed = "rfq.submission_closed";

    // T3-36: three states became reachable, and §3.1 names a notification for each transition into
    // and out of them.
    public const string RfqClarificationRequested = "rfq.clarification_requested";
    public const string RfqClarificationResolved = "rfq.clarification_resolved";
    public const string RfqShortlistingStarted = "rfq.shortlisting_started";
    public const string RfqRecommendationRecorded = "rfq.recommendation_recorded";

    // ---- Evaluation (BUSINESS-PROCESSES §3.3) ----
    public const string EvaluationOpened = "evaluation.opened";
    public const string EvaluatorSubmitted = "evaluation.evaluator_submitted";
    public const string EvaluationConsolidated = "evaluation.consolidated";
    public const string EvaluationFinalized = "evaluation.finalized";
    public const string EvaluationReopened = "evaluation.reopened";
    public const string EvaluatorRecused = "evaluation.evaluator_recused";

    // ---- Award (BUSINESS-PROCESSES §3.4) ----
    public const string AwardRecommended = "award.recommended";
    public const string AwardRoutedForApproval = "award.routed_for_approval";
    public const string AwardApproved = "award.approved";
    public const string AwardRejected = "award.rejected";
    public const string AwardReRecommended = "award.re_recommended";
    public const string AwardErpSynced = "award.erp_synced";
    public const string AwardErpFailed = "award.erp_failed";

    // ---- Proposal (BUSINESS-PROCESSES §3.2) ----
    public const string ProposalWithdrawn = "proposal.withdrawn";

    /// <summary>§4.1: "UnderReview -&gt; ClarificationRequested ... Email + in-app to supplier".</summary>
    public const string ProposalClarificationRequested = "proposal.clarification_requested";

    /// <summary>§4.1: "ClarificationRequested -&gt; Revised ... In-app to committee".</summary>
    public const string ProposalRevised = "proposal.revised";

    /// <summary>Both directions of the catalogue gate compare against this set.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        RfqSubmittedForReview, RfqReturnedForEdits, RfqApproved, RfqSubmissionOpened, RfqSubmissionClosed,
        RfqClarificationRequested, RfqClarificationResolved, RfqShortlistingStarted, RfqRecommendationRecorded,
        EvaluationOpened, EvaluatorSubmitted, EvaluationConsolidated, EvaluationFinalized,
        EvaluationReopened, EvaluatorRecused,
        AwardRecommended, AwardRoutedForApproval, AwardApproved, AwardRejected, AwardReRecommended,
        AwardErpSynced, AwardErpFailed,
        ProposalWithdrawn,
        ProposalClarificationRequested,
        ProposalRevised,
    };
}
