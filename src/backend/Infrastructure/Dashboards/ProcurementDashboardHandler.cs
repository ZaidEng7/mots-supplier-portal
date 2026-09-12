using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
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
            // A-7: counted from the ROWS rather than from the pipeline group totals. The pipeline
            // groups by state only, so summing it gave every holder of a permission the whole
            // organization's count for that state - which is what made this tile a duplicate of
            // Active RFQs for a single-officer organization. It now needs the per-row owner, so it is
            // its own query.
            AwaitingMyAction: await AwaitingMyActionAsync(rfqs, awaitingStates, ct),
            PendingApprovals: await PendingApprovalsAsync(organizationId, ct),
            AwardsInProgress: await db.Awards.AsNoTracking()
                .CountAsync(a => db.Rfqs.Any(r => r.Id == a.RfqId && r.OrganizationId == organizationId)
                                 && a.State != AwardState.Awarded, ct));

        return new ProcurementDashboardDto(
            kpis,
            [.. pipeline
                .OrderBy(p => p.State)
                .Select(p => new PipelineColumnDto(p.State.ToString(), p.Count, p.NearestDeadline))],
            await TasksAsync(db, rfqs, ct),
            // §10: "Manager also gets an Approvals card". Decided from the permission rather than the
            // role name, so a tenant that moves approval to another role keeps a correct dashboard.
            ShowsApprovals: scope.HasPermission(Permissions.RfqApprove) || scope.HasPermission(Permissions.AwardApprove));
    }

    /// <summary>
    /// A-7: the RFQs in a state whose next action needs a permission this caller holds, AND which are
    /// this caller's to act on.
    ///
    /// <para>Three ways an RFQ is the caller's: they own it; the current review pass named them as its
    /// approver (an approver does not own the RFQ, and if this only looked at ownership the manager's
    /// tile would read zero for the one state they are the bottleneck of); or it has no owner at all,
    /// in which case it belongs to whoever can act on it - see the class note on why the unowned case
    /// is deliberately wide.</para>
    /// </summary>
    private Task<int> AwaitingMyActionAsync(IQueryable<Rfq> rfqs, List<RfqState> awaitingStates, CancellationToken ct)
    {
        if (awaitingStates.Count == 0) return Task.FromResult(0);

        var userId = scope.UserId;
        return rfqs
            .Where(r => awaitingStates.Contains(r.State))
            .CountAsync(r => r.OwnerUserId == null
                             || r.OwnerUserId == userId
                             || r.Approvals.Any(a => a.Decision == null && a.AssignedApproverUserId == userId), ct);
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

    /// <summary>
    /// §10's lower-left panel: "submissions closing, evaluations due, recommendations pending" - and,
    /// since T-038, the two other deadlines FEAT-17.5 names.
    ///
    /// <para>FEAT-17.5 asks for "submission/clarification/expiry" consolidated. The panel carried the
    /// first and neither of the others, which left out the two dates a buyer can actually MISS: a
    /// clarification window closing on an unanswered question, and a bid expiring before the award is
    /// signed.</para>
    /// </summary>
    private static async Task<List<DashboardTaskDto>> TasksAsync(AppDbContext db, IQueryable<Rfq> rfqs, CancellationToken ct)
    {
        var rows = await rfqs
            .Where(r => r.State == RfqState.SubmissionOpen
                        || r.State == RfqState.UnderEvaluation
                        || r.State == RfqState.Shortlisting
                        || r.State == RfqState.Recommendation
                        // T-038: the clarification window is its own state, and its deadline lives on
                        // the RFQ. A tender sitting here with nobody answering is the case the panel
                        // exists to surface.
                        || r.State == RfqState.Clarification)
            .Select(r => new
            {
                r.Id, r.ReferenceCode, r.TitleAr, r.TitleEn, r.State,
                r.SubmissionClosesAt, r.EvaluationTargetDate, r.ClarificationDeadlineAt,
            })
            .ToListAsync(ct);

        var tasks = rows
            .Select(r => new DashboardTaskDto(
                r.ReferenceCode, r.TitleAr, r.TitleEn,
                Kind: r.State switch
                {
                    RfqState.SubmissionOpen => DashboardTaskKinds.SubmissionClosing,
                    RfqState.UnderEvaluation => DashboardTaskKinds.EvaluationDue,
                    RfqState.Clarification => DashboardTaskKinds.ClarificationClosing,
                    _ => DashboardTaskKinds.RecommendationPending,
                },
                Due: r.State switch
                {
                    RfqState.SubmissionOpen => r.SubmissionClosesAt,
                    RfqState.Clarification => r.ClarificationDeadlineAt,
                    _ => r.EvaluationTargetDate,
                }))
            .ToList();

        tasks.AddRange(await BidValidityTasksAsync(db, rows.Select(r => r.Id).ToList(), ct));

        return [.. tasks
            // Soonest first, and rows with no date last rather than first - a task with no deadline
            // is not the most urgent thing on the screen.
            .OrderBy(t => t.Due is null)
            .ThenBy(t => t.Due)];
    }

    /// <summary>
    /// T-038/FEAT-17.5's "expiry": the earliest validity date among the bids on a tender.
    ///
    /// <para>A tender whose leading bid expires before the award is executed has to go back to the
    /// supplier for an extension, or be re-run. That is the largest consequence on this panel and
    /// nothing showed it.</para>
    ///
    /// <para><b>Consolidated or later, only.</b> Validity is a commercial term, and BRULE-058 keeps
    /// commercial values out of every buyer-side read until the evaluation consolidates - the same
    /// gate the comparison matrix applies. Before that point the row is absent rather than dateless,
    /// because "a bid on this tender expires soon" is itself the disclosure.</para>
    /// </summary>
    private static async Task<List<DashboardTaskDto>> BidValidityTasksAsync(
        AppDbContext db, List<Guid> rfqIds, CancellationToken ct)
    {
        if (rfqIds.Count == 0) return [];

        var consolidatedRfqIds = await db.Evaluations.AsNoTracking()
            .Where(e => rfqIds.Contains(e.RfqId)
                        && (e.State == EvaluationState.Consolidated || e.State == EvaluationState.Finalized))
            .Select(e => e.RfqId)
            .ToListAsync(ct);

        if (consolidatedRfqIds.Count == 0) return [];

        var bids = await db.Proposals.AsNoTracking()
            .Where(p => consolidatedRfqIds.Contains(p.RfqId)
                        && p.ValidityEnd != null
                        && ProposalStates.UnderComparison.Contains(p.State))
            .Select(p => new { p.RfqId, p.ValidityEnd })
            .ToListAsync(ct);

        var rfqs = await db.Rfqs.AsNoTracking()
            .Where(r => consolidatedRfqIds.Contains(r.Id))
            .Select(r => new { r.Id, r.ReferenceCode, r.TitleAr, r.TitleEn })
            .ToListAsync(ct);

        return [.. bids
            // One row per tender, at its EARLIEST expiry: the panel is a list of deadlines a person
            // acts on, and five rows for five bids on one tender would bury the other tenders.
            .GroupBy(b => b.RfqId)
            .Select(g => new { RfqId = g.Key, Earliest = g.Min(b => b.ValidityEnd!.Value) })
            .Join(rfqs, b => b.RfqId, r => r.Id, (b, r) => new DashboardTaskDto(
                r.ReferenceCode, r.TitleAr, r.TitleEn,
                DashboardTaskKinds.BidValidityExpiring,
                // A date, not an instant: validity is recorded as a calendar day, and the deadline is
                // the end of it.
                new DateTimeOffset(b.Earliest.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)))];
    }
}
