// The procurement officer's dashboard: the pipeline board, the tiles, and the deadlines panel.
//
//
// EVERY NUMBER IS SCOPED TO THE ORGANIZATION IN THE QUERY, NOT FILTERED AFTERWARDS
//
// A dashboard is the widest cross-aggregate read in the product, and cross-organization leakage is the risk
// register's only critical entry.
//
// A COUNT leaks even when no row is shown: a tile reading fourteen active tenders that included another
// organization's rows discloses volume, and no list-level test would catch it.
//
// So the organization is the first clause of every query here, and the tests assert the numbers.
//
// A caller with no organization has no dashboard and is told so as a not-found rather than shown an empty one. An
// empty dashboard would assert that the organization exists and is idle.
//
//
// THE PERIOD FILTER APPLIES TO PUBLICATION, AND NEVER HIDES A DRAFT
//
// Publication is the only date on a tender that marks when it entered the market.
//
// A tender that has NEVER been published has no value to filter on and is always included. Excluding it would
// empty the board's left-hand columns whenever a period was chosen, which is the opposite of what an officer
// filtering by quarter is asking for.
//
//
// "MY WORK" IS COUNTED FROM THE ROWS, NOT FROM THE PIPELINE TOTALS
//
// The pipeline groups by state only, so summing it gave every holder of a permission the whole organization's
// count for that state, which is what made this tile a duplicate of the active-tenders one in a single-officer
// organization.
//
// It needs the per-row owner, so it is its own query.
//
// Three things make a tender the caller's: they own it; the current review pass named them as its approver, which
// is not ownership and without which a manager's tile would read zero for the one state they are the bottleneck
// of; or it has no owner at all, in which case it belongs to whoever can act on it. The unowned case is
// deliberately wide.
//
// Whether a manager sees the approvals card is decided from the permission rather than from a role name, so a
// ministry that moves approval to another role keeps a correct dashboard.
//
//
// THE DEADLINES PANEL CARRIES THE TWO DATES A BUYER CAN ACTUALLY MISS
//
// It asked for submission, clarification and expiry consolidated, and carried the first and neither of the others.
//
// A clarification window closing on an unanswered question is its own state with its own deadline, and a tender
// sitting there with nobody answering is the case the panel exists to surface.
//
// A bid expiring before the award is signed means going back to the supplier for an extension, or re-running the
// tender. That is the largest consequence on this panel and nothing showed it.
//
// Expiry appears only once the evaluation has consolidated, because validity is a commercial term and commercial
// values stay out of every buyer-side read until then, which is the same gate the comparison matrix applies.
// Before that point the row is absent rather than dateless, because "a bid on this tender expires soon" is itself
// the disclosure.
//
// One row per tender, at its earliest expiry: the panel is a list of deadlines a person acts on, and five rows for
// five bids on one tender would bury the other tenders.
//
// Validity is a calendar day rather than an instant, so the deadline is the end of it.
//
// Soonest first, with rows that have no date last rather than first. A task with no deadline is not the most
// urgent thing on the screen.

namespace MotsSupplierPortal.Infrastructure.Dashboards;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ProcurementDashboardHandler(AppDbContext db, IScopeContext scope) : IProcurementDashboardHandler
{
    private static readonly TimeSpan ClosingWindow = TimeSpan.FromDays(7);

    private static readonly RfqState[] Terminal = [RfqState.Completed, RfqState.Cancelled];

    public async Task<ProcurementDashboardDto?> HandleAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        if (scope.OrganizationId is not { } organizationId) return null;

        var now = DateTimeOffset.UtcNow;

        var rfqs = db.Rfqs.AsNoTracking().Where(r => r.OrganizationId == organizationId);

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
            ShowsApprovals: scope.HasPermission(Permissions.RfqApprove) || scope.HasPermission(Permissions.AwardApprove));
    }

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

    private async Task<int> PendingApprovalsAsync(Guid organizationId, CancellationToken ct)
    {
        var rfqApprovals = await db.Rfqs.AsNoTracking()
            .CountAsync(r => r.OrganizationId == organizationId && r.State == RfqState.InternalReview, ct);

        var awardApprovals = await db.Awards.AsNoTracking()
            .CountAsync(a => a.State == AwardState.PendingApproval
                             && db.Rfqs.Any(r => r.Id == a.RfqId && r.OrganizationId == organizationId), ct);

        return rfqApprovals + awardApprovals;
    }

    private static async Task<List<DashboardTaskDto>> TasksAsync(AppDbContext db, IQueryable<Rfq> rfqs, CancellationToken ct)
    {
        var rows = await rfqs
            .Where(r => r.State == RfqState.SubmissionOpen
                        || r.State == RfqState.UnderEvaluation
                        || r.State == RfqState.Shortlisting
                        || r.State == RfqState.Recommendation
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
            .OrderBy(t => t.Due is null)
            .ThenBy(t => t.Due)];
    }

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
            .GroupBy(b => b.RfqId)
            .Select(g => new { RfqId = g.Key, Earliest = g.Min(b => b.ValidityEnd!.Value) })
            .Join(rfqs, b => b.RfqId, r => r.Id, (b, r) => new DashboardTaskDto(
                r.ReferenceCode, r.TitleAr, r.TitleEn,
                DashboardTaskKinds.BidValidityExpiring,
                new DateTimeOffset(b.Earliest.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)))];
    }
}
