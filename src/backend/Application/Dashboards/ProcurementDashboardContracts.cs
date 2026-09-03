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

/// <summary>§10's task kinds: "submissions closing, evaluations due, recommendations pending".</summary>
public static class DashboardTaskKinds
{
    public const string SubmissionClosing = "SubmissionClosing";
    public const string EvaluationDue = "EvaluationDue";
    public const string RecommendationPending = "RecommendationPending";
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
/// <para><b>This is what "Awaiting my action" is scoped to, and it is an invention</b> - §10 names
/// the tile and defines nothing. It cannot be ownership: EPIC-15 established that nothing records
/// which officer owns an RFQ, so there is no per-user "mine". Falling back to org-wide would make
/// the tile a duplicate of Active RFQs without saying so, which is the one answer that is definitely
/// wrong.</para>
///
/// <para>So: an RFQ is awaiting THIS user's action when the caller holds the permission its current
/// state's next transition requires. An officer and a manager looking at the same organization see
/// different numbers, which is the property that makes the tile mean anything.</para>
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
