// The two approval queues: tenders waiting to be approved for publication, and awards waiting for a decision.
//
//
// NOT A PERSONAL QUEUE, AND THE SCREEN MUST NOT IMPLY ONE
//
// Nothing in the identity domain resolves a single named approver from the approval permission, which is the gap
// the notifications work reported and did not close.
//
// So both queues are per role and organization: this is the work waiting for SOMEONE with your permissions in your
// organization, not work assigned to you.
//
//
// SEGREGATION OF DUTIES IS APPLIED HERE, NOT ONLY AT THE WRITE
//
// The write refuses an approver who recommended the award themselves. A queue that listed that award anyway would
// be offering a manager work they will be refused when they click it.
//
// So it is filtered out of the queue rather than refused after the click.
//
//
// THE WAIT IS THE REAL ONE
//
// A tender records when it entered its current state, so the age is measured from that rather than from its
// creation, which would read as "waiting three weeks" for a tender drafted three weeks ago and submitted
// yesterday.
//
// It is still nullable, and honestly so: a tender that entered review before that column existed has no recorded
// instant, and the row says nothing rather than guessing.

namespace MotsSupplierPortal.Infrastructure.Dashboards;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ApprovalQueuesHandler(AppDbContext db, IScopeContext scope) : IApprovalQueuesHandler
{
    public async Task<ApprovalQueuesDto?> HandleAsync(CancellationToken ct)
    {
        if (scope.OrganizationId is not { } organizationId) return null;

        var rfqApprovals = await db.Rfqs.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && r.State == RfqState.InternalReview)
            .Select(r => new ApprovalQueueItemDto(
                r.ReferenceCode, r.TitleAr, r.TitleEn, r.State.ToString(),
                r.StateChangedAt,
                $"/api/v1/rfqs/{r.ReferenceCode}"))
            .ToListAsync(ct);

        var awardApprovals = await db.Awards.AsNoTracking()
            .Where(a => a.State == AwardState.PendingApproval)
            .Where(a => db.Rfqs.Any(r => r.Id == a.RfqId && r.OrganizationId == organizationId))
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
