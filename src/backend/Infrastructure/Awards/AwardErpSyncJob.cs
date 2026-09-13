// The reconciliation half of sending an award to the external purchasing system.
//
// The transactional part already happened: the outbox row was written in the same commit as the award, which the
// written rule requires. This job is what actually calls the adapter and writes the result back onto the award.
//
// The generic dispatcher cannot do that, because it only updates the outbox row's own status and never the
// aggregate the message describes.
//
//
// IT NEVER BLOCKS THE AWARD
//
// It runs on its own schedule, entirely decoupled from the request that issued the award. By the time it runs,
// the award is already issued and stays issued no matter what happens here, which is why the integration status
// is a separate field on the award.
//
// Retry with backoff IS this job's own recurring cadence, on the same reasoning as the other recurring jobs: a
// failed award is picked up again on the next run rather than needing a bespoke scheduler. A manual retry exists
// for an administrator who does not want to wait for the schedule.
//
// A failure alerts the platform administrators rather than the buying body, because an integration failure is
// platform-level. That notification exists precisely BECAUSE the award stands: the failure must not undo it, and
// the alert is how somebody finds out.

namespace MotsSupplierPortal.Infrastructure.Awards;

using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class AwardErpSyncJob(AppDbContext db, IErpPurchaseOrderAdapter adapter, IAuditLogger auditLogger, ILogger<AwardErpSyncJob> logger)
{
    public const int BatchSize = 50;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var pending = await db.Awards
            .Where(a => a.State == AwardState.Awarded && a.ErpSyncStatus == ErpSyncStatus.Requested)
            .OrderBy(a => a.AwardedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var award in pending)
        {
            var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.Id == award.RfqId, ct);
            if (rfq is null) continue;

            try
            {
                var externalRef = await adapter.CreatePurchaseOrderAsync(award.Id, rfq.ReferenceCode, ct);
                award.MarkErpSynced(externalRef);
                if (rfq.State == RfqState.Awarded) rfq.Complete();

                NotificationOutbox.EnqueueMany(db, NotificationTypes.AwardErpSynced,
                    await NotificationRecipients.CommitteeAsync(db, rfq.OrganizationId, ct),
                    $"{NotificationTypes.AwardErpSynced}:{award.Id}",
                    new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["awardId"] = award.Id.ToString() });

                await auditLogger.LogAsync("Award", award.Id, "award.erp_po_synced", actorLabel: "system",
                    referenceCode: rfq.ReferenceCode, toState: nameof(ErpSyncStatus.Synced), ct: ct);
                await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_completed", actorLabel: "system",
                    referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.Awarded), toState: nameof(RfqState.Completed), ct: ct);
            }
            catch (Exception ex)
            {
                award.MarkErpFailed();
                NotificationOutbox.EnqueueMany(db, NotificationTypes.AwardErpFailed,
                    await NotificationRecipients.SystemAdminsAsync(db, ct),
                    $"{NotificationTypes.AwardErpFailed}:{award.Id}:{award.ErpRetryCount}",
                    new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["awardId"] = award.Id.ToString() });

                await auditLogger.LogAsync("Award", award.Id, "award.erp_po_failed", actorLabel: "system",
                    referenceCode: rfq.ReferenceCode, toState: nameof(ErpSyncStatus.Failed), ct: ct);
                logger.LogError(ex, "Award {AwardId} ERP Purchase Order sync failed (attempt {RetryCount})", award.Id, award.ErpRetryCount);
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
