using MotsSupplierPortal.Domain.Rfqs;

namespace MotsSupplierPortal.Application.Dashboards;

/// <summary>SCR-400's KPI row (SCREEN-SPECIFICATIONS.md §10), all five tiles, all org-scoped.</summary>
public sealed record ProcurementKpisDto(
    int ActiveRfqs,
    int ClosingThisWeek,
    int AwaitingMyAction,
    int PendingApprovals,
    int AwardsInProgress);

/// <summary>One column of §10's pipeline board: "RFQs grouped by RfqState … with counts + deadlines".</summary>
public sealed record PipelineColumnDto(string State, int Count, DateTimeOffset? NearestDeadline);

/// <summary>One row of §10's "Deadlines &amp; tasks" panel.</summary>
public sealed record DashboardTaskDto(string RfqReferenceCode, string TitleAr, string TitleEn, string Kind, DateTimeOffset? Due);

/// <summary>
/// §10's task kinds: "submissions closing, evaluations due, recommendations pending" - plus the two
/// FEAT-17.5 names and this panel did not carry.
///
/// <para><b>T-038.</b> FEAT-17.5 asks for a consolidated deadline view of
/// "submission/clarification/expiry". The panel had the first and neither of the others, so the two
/// dates a buyer can actually MISS were the two it did not show: a clarification window that closes
/// with a supplier's question unanswered, and a bid whose validity lapses before the award is
/// signed.</para>
/// </summary>
public static class DashboardTaskKinds
{
    public const string SubmissionClosing = "SubmissionClosing";
    public const string EvaluationDue = "EvaluationDue";
    public const string RecommendationPending = "RecommendationPending";

    /// <summary>FEAT-17.5's "clarification". The window closes whether or not anyone answered.</summary>
    public const string ClarificationClosing = "ClarificationClosing";

    /// <summary>
    /// FEAT-17.5's "expiry", read as the expiry a BUYER can act on: a bid's own validity date.
    ///
    /// <para>A tender whose leading bid expires before the award is executed has to go back to the
    /// supplier for an extension, or be re-run. It is the deadline on this screen with the largest
    /// consequence and it was the one nothing showed.</para>
    ///
    /// <para><b>Only once the evaluation is consolidated.</b> Validity is a commercial term, and
    /// BRULE-058 keeps commercial values out of every buyer-side read until consolidation - the same
    /// gate the comparison matrix applies. Before that point this kind does not appear at all, rather
    /// than appearing with the date withheld, because a row that says "a bid expires soon" is itself
    /// the disclosure.</para>
    /// </summary>
    public const string BidValidityExpiring = "BidValidityExpiring";
}

public sealed record ProcurementDashboardDto(
    ProcurementKpisDto Kpis,
    IReadOnlyList<PipelineColumnDto> Pipeline,
    IReadOnlyList<DashboardTaskDto> Tasks,
    /// <summary>§10 gives the manager an Approvals card; an officer does not get one. Sent rather
    /// than inferred client-side, so the affordance and the API agree about who may approve.</summary>
    bool ShowsApprovals);

public interface IProcurementDashboardHandler
{
    Task<ProcurementDashboardDto?> HandleAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
}

/// <summary>
/// The next action each RFQ state is waiting for, and the permission it needs.
///
/// <para><b>Half of this tile's definition is an invention and half of it is now ownership.</b> §10
/// names the tile and defines nothing. Until A-7 nothing recorded which officer owned an RFQ, so
/// "mine" could only be approximated by permission: an RFQ is awaiting THIS user's action when the
/// caller holds the permission its current state's next transition requires. That half stays - it is
/// what makes an officer and a manager see different numbers for the same organization.</para>
///
/// <para><b>A-7 adds the other half:</b> the RFQ also has to be ASSIGNED to the caller - they own it,
/// or (in InternalReview) the pass named them as its approver. An RFQ with no owner counts for
/// everyone holding the permission, which is the same fallback
/// <c>NotificationRecipients.RfqOwnerAsync</c> makes and for the same reason: an unowned RFQ that
/// appears on nobody's tile is an RFQ nobody is going to pick up. See DECISIONS-TAKEN.md D-38.</para>
/// </summary>
public static class AwaitingActionPermissions
{
    public static string? For(RfqState state) => state switch
    {
        RfqState.Draft => Domain.Identity.Permissions.RfqSubmitReview,
        RfqState.InternalReview => Domain.Identity.Permissions.RfqApprove,
        RfqState.Approved => Domain.Identity.Permissions.RfqPublish,
        RfqState.SubmissionClosed => Domain.Identity.Permissions.EvaluationOpen,
        RfqState.UnderEvaluation => Domain.Identity.Permissions.EvaluationConsolidate,
        RfqState.Clarification => Domain.Identity.Permissions.RfqClarify,
        RfqState.Shortlisting => Domain.Identity.Permissions.AwardRecommend,
        RfqState.Recommendation => Domain.Identity.Permissions.AwardRecommend,
        RfqState.AwardApproval => Domain.Identity.Permissions.AwardApprove,

        // Waiting on the clock, on suppliers, or on nobody: Published and SubmissionOpen advance by
        // deadline, and Awarded/Completed/Cancelled are done. Not anyone's action.
        _ => null,
    };
}
