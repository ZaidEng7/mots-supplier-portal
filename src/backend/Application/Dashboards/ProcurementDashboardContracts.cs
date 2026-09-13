// The vocabulary for the buying officer's dashboard: the figures across the top, the pipeline board, and the
// panel of things with dates attached.
//
// Everything is scoped to the caller's own organization.
//
// The pipeline board is one column per tender state, with a count and the nearest deadline in it.
//
//
// THE FIVE TASK KINDS, AND WHY TWO OF THEM WERE MISSING
//
// The written requirement asks for a consolidated view of submission, clarification and expiry dates. The
// panel had the first and neither of the others.
//
// So the two dates a buyer can actually miss were the two it did not show: a clarification window that
// closes with a supplier's question unanswered, and a bid whose validity lapses before the award is signed.
//
// Expiry is read as the expiry a buyer can act on, which is a bid's own validity date. A tender whose
// leading bid expires before the award is executed has to go back to the market, and that is the buyer's
// problem rather than the supplier's.

namespace MotsSupplierPortal.Application.Dashboards;

using MotsSupplierPortal.Domain.Rfqs;

public sealed record ProcurementKpisDto(
    int ActiveRfqs,
    int ClosingThisWeek,
    int AwaitingMyAction,
    int PendingApprovals,
    int AwardsInProgress);

public sealed record PipelineColumnDto(string State, int Count, DateTimeOffset? NearestDeadline);

public sealed record DashboardTaskDto(string RfqReferenceCode, string TitleAr, string TitleEn, string Kind, DateTimeOffset? Due);

public static class DashboardTaskKinds
{
    public const string SubmissionClosing = "SubmissionClosing";
    public const string EvaluationDue = "EvaluationDue";
    public const string RecommendationPending = "RecommendationPending";

    public const string ClarificationClosing = "ClarificationClosing";

    public const string BidValidityExpiring = "BidValidityExpiring";
}

public sealed record ProcurementDashboardDto(
    ProcurementKpisDto Kpis,
    IReadOnlyList<PipelineColumnDto> Pipeline,
    IReadOnlyList<DashboardTaskDto> Tasks,
    bool ShowsApprovals);

public interface IProcurementDashboardHandler
{
    Task<ProcurementDashboardDto?> HandleAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
}

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

        _ => null,
    };
}
