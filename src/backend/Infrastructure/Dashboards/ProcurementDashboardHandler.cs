using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Dashboards;

/// <summary>
/// SCR-400 / FR-DSH-008. §10: "scoped to <c>OrganizationId</c>".
///
/// <para><b>Every number here is org-scoped in the query, not filtered afterwards.</b> A dashboard is
/// the widest cross-aggregate read in the product and RISK-004 is the risk register's only Critical -
/// and a COUNT leaks even when no row is shown: "Active RFQs: 14" that includes another
/// organization's rows discloses volume, and no list-level test would catch it. The organization
/// predicate is therefore the first clause of every query below, and the tests assert the numbers.</para>
/// </summary>
public sealed class ProcurementDashboardHandler(AppDbContext db, IScopeContext scope) : IProcurementDashboardHandler
{
    /// <summary>§10's "Closing this week".</summary>
    private static readonly TimeSpan ClosingWindow = TimeSpan.FromDays(7);

    /// <summary>Terminal states are not "active" and do not appear on the board's working columns.</summary>
    private static readonly RfqState[] Terminal = [RfqState.Completed, RfqState.Cancelled];

    public async Task<ProcurementDashboardDto?> HandleAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        // §9.2: a caller with no organization has no dashboard, and says so as a 404 rather than an
        // empty one - an empty dashboard would assert that the organization exists and is idle.
        if (scope.OrganizationId is not { } organizationId) return null;

        var now = DateTimeOffset.UtcNow;

        var rfqs = db.Rfqs.AsNoTracking().Where(r => r.OrganizationId == organizationId);

        // §10's period filter, applied to PublishedAt - the only date on an RFQ that marks when it
        // entered the market, and the field §12-A/C3 added for exactly this kind of question.
        //
        // An RFQ that has NEVER been published has no value to filter on, and is always included.
        // Excluding it would empty the board's left-hand columns whenever a period was chosen -
        // Draft, InternalReview and Approved would vanish - which is the opposite of what an officer
        // filtering "this quarter" is asking for. Stated rather than left to be discovered.
        if (from is { } start) rfqs = rfqs.Where(r => r.PublishedAt == null || r.PublishedAt >= start);
        if (to is { } end) rfqs = rfqs.Where(r => r.PublishedAt == null || r.PublishedAt <= end);

        var pipeline = await rfqs
            .Where(r => !Terminal.Contains(r.State))
            .GroupBy(r => r.State)
            .Select(g => new
            {
                State = g.Key,
                Count = g.Count(),
                NearestDeadline = g.Min(r => r.SubmissionClosesAt),
            })
            .ToListAsync(ct);

        var awaitingStates = Enum.GetValues<RfqState>()
            .Where(state => AwaitingActionPermissions.For(state) is { } permission && scope.HasPermission(permission))
            .ToList();

        var kpis = new ProcurementKpisDto(
            ActiveRfqs: pipeline.Sum(p => p.Count),
            ClosingThisWeek: await rfqs.CountAsync(
                r => r.State == RfqState.SubmissionOpen
                     && r.SubmissionClosesAt != null
                     && r.SubmissionClosesAt >= now
                     && r.SubmissionClosesAt <= now + ClosingWindow, ct),
            AwaitingMyAction: pipeline.Where(p => awaitingStates.Contains(p.State)).Sum(p => p.Count),
            PendingApprovals: await PendingApprovalsAsync(organizationId, ct),
            AwardsInProgress: await db.Awards.AsNoTracking()
                .CountAsync(a => db.Rfqs.Any(r => r.Id == a.RfqId && r.OrganizationId == organizationId)
                                 && a.State != AwardState.Awarded, ct));

        return new ProcurementDashboardDto(
            kpis,
            [.. pipeline
                .OrderBy(p => p.State)
                .Select(p => new PipelineColumnDto(p.State.ToString(), p.Count, p.NearestDeadline))],
            await TasksAsync(rfqs, ct),
            // §10: "Manager also gets an Approvals card". Decided from the permission rather than the
            // role name, so a tenant that moves approval to another role keeps a correct dashboard.
            ShowsApprovals: scope.HasPermission(Permissions.RfqApprove) || scope.HasPermission(Permissions.AwardApprove));
    }

    /// <summary>
    /// §10's "Pending approvals" tile, which SCR-401 then splits into its two queues: RFQs waiting to
    /// be approved for publication, and awards waiting for an approval decision.
    /// </summary>
    private async Task<int> PendingApprovalsAsync(Guid organizationId, CancellationToken ct)
    {
        var rfqApprovals = await db.Rfqs.AsNoTracking()
            .CountAsync(r => r.OrganizationId == organizationId && r.State == RfqState.InternalReview, ct);

        var awardApprovals = await db.Awards.AsNoTracking()
            .CountAsync(a => a.State == AwardState.PendingApproval
                             && db.Rfqs.Any(r => r.Id == a.RfqId && r.OrganizationId == organizationId), ct);

        return rfqApprovals + awardApprovals;
    }

    /// <summary>§10's lower-left panel: "submissions closing, evaluations due, recommendations pending".</summary>
    private static async Task<List<DashboardTaskDto>> TasksAsync(IQueryable<Rfq> rfqs, CancellationToken ct)
    {
        var rows = await rfqs
            .Where(r => r.State == RfqState.SubmissionOpen
                        || r.State == RfqState.UnderEvaluation
                        || r.State == RfqState.Shortlisting
                        || r.State == RfqState.Recommendation)
            .Select(r => new
            {
                r.ReferenceCode, r.TitleAr, r.TitleEn, r.State,
                r.SubmissionClosesAt, r.EvaluationTargetDate,
            })
            .ToListAsync(ct);

        return [.. rows
            .Select(r => new DashboardTaskDto(
                r.ReferenceCode, r.TitleAr, r.TitleEn,
                Kind: r.State switch
                {
                    RfqState.SubmissionOpen => DashboardTaskKinds.SubmissionClosing,
                    RfqState.UnderEvaluation => DashboardTaskKinds.EvaluationDue,
                    _ => DashboardTaskKinds.RecommendationPending,
                },
                Due: r.State == RfqState.SubmissionOpen ? r.SubmissionClosesAt : r.EvaluationTargetDate))
            // Soonest first, and rows with no date last rather than first - a task with no deadline
            // is not the most urgent thing on the screen.
            .OrderBy(t => t.Due is null)
            .ThenBy(t => t.Due)];
    }
}
