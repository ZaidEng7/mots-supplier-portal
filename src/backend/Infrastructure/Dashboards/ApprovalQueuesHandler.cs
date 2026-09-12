using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Dashboards;

/// <summary>
/// SCR-401's two queues.
///
/// <para><b>Not a personal queue, and the screen must not imply one.</b> Nothing in the Identity
/// domain resolves a single named approver from the <c>award.approve</c> claim - the gap EPIC-15
/// reported and did not close - so both queues are per-role-and-organization: this is the work
/// waiting for SOMEONE with your permissions in your organization, not work assigned to you.</para>
///
/// <para><b>Segregation of duties is applied here, not just at the write.</b> EPIC-14 refuses an
/// approver who recommended the award themselves. A queue that lists that award anyway would be
/// offering a manager work they will be refused when they click it - the same shape of defect as
/// PR #90's, one screen along.</para>
/// </summary>
public sealed class ApprovalQueuesHandler(AppDbContext db, IScopeContext scope) : IApprovalQueuesHandler
{
    public async Task<ApprovalQueuesDto?> HandleAsync(CancellationToken ct)
    {
        if (scope.OrganizationId is not { } organizationId) return null;

        var rfqApprovals = await db.Rfqs.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && r.State == RfqState.InternalReview)
            .Select(r => new ApprovalQueueItemDto(
                r.ReferenceCode, r.TitleAr, r.TitleEn, r.State.ToString(),
                // T-031 closed this. The RFQ now records when it entered its current state, so the
                // wait is the real one rather than CreatedAt - which would have read as "waiting three
                // weeks" for a tender drafted three weeks ago and submitted yesterday.
                //
                // Still nullable, and still honestly so: a tender that entered review before the
                // column existed has no recorded instant, and the row says nothing rather than
                // guessing.
                r.StateChangedAt,
                $"/api/v1/rfqs/{r.ReferenceCode}"))
            .ToListAsync(ct);

        var awardApprovals = await db.Awards.AsNoTracking()
            .Where(a => a.State == AwardState.PendingApproval)
            .Where(a => db.Rfqs.Any(r => r.Id == a.RfqId && r.OrganizationId == organizationId))
            // EPIC-14/BRULE: the approver may not be the recommender. Filtered out of the queue so it
            // is never offered, rather than refused after the click.
            .Where(a => a.RecommendedByUserId != scope.UserId)
            .Select(a => new ApprovalQueueItemDto(
                db.Rfqs.Where(r => r.Id == a.RfqId).Select(r => r.ReferenceCode).First(),
                db.Rfqs.Where(r => r.Id == a.RfqId).Select(r => r.TitleAr).First(),
                db.Rfqs.Where(r => r.Id == a.RfqId).Select(r => r.TitleEn).First(),
                a.State.ToString(),
                a.RecommendedAt,
                $"/api/v1/rfqs/{db.Rfqs.Where(r => r.Id == a.RfqId).Select(r => r.ReferenceCode).First()}/award"))
            .ToListAsync(ct);

        return new ApprovalQueuesDto(rfqApprovals, awardApprovals);
    }
}
