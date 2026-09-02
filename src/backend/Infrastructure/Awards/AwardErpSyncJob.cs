using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Awards;

/// <summary>FEAT-14.5/14.6/FR-AWD-005/006, BRULE-077/078/079: the reconciliation half of the
/// "Outbox -&gt; ERP PO" flow. The transactional emit itself (the OutboxMessage row, written in the
/// SAME SaveChanges as ExecuteAward - BRULE-078's own "same transaction" requirement) already
/// happened synchronously in ExecuteAwardHandler; this job is what actually calls the (stubbed) ERP
/// adapter and writes the result back onto the Award aggregate - something the generic, transport-
/// agnostic OutboxDispatcher cannot do, since it only updates the OutboxMessage row's own
/// SyncStatus, never the domain aggregate the message describes.
///
/// <para><b>Never blocks the award (BRULE-077).</b> This job runs asynchronously, on its own
/// recurring schedule, entirely decoupled from the HTTP request that issued the award - by the time
/// this job ever runs, Award.State is already Awarded and stays Awarded no matter what happens
/// below (see Award.cs's own doc comment on why ErpSyncStatus is a separate field).</para>
///
/// <para><b>Retry-with-backoff (BRULE-078) is this job's own recurring cadence, same reasoning as
/// OutboxDispatcher/RfqTimelineJob</b>: a Failed award is picked up again on the next run rather
/// than requiring a bespoke backoff scheduler - a manual RetryErpSync (integration.retry) exists for
/// an admin who does not want to wait for the schedule.</para></summary>
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

                await auditLogger.LogAsync("Award", award.Id, "award.erp_po_synced", actorLabel: "system",
                    referenceCode: rfq.ReferenceCode, toState: nameof(ErpSyncStatus.Synced), ct: ct);
                await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_completed", actorLabel: "system",
                    referenceCode: rfq.ReferenceCode, fromState: nameof(RfqState.Awarded), toState: nameof(RfqState.Completed), ct: ct);
            }
            catch (Exception ex)
            {
                award.MarkErpFailed();
                await auditLogger.LogAsync("Award", award.Id, "award.erp_po_failed", actorLabel: "system",
                    referenceCode: rfq.ReferenceCode, toState: nameof(ErpSyncStatus.Failed), ct: ct);
                logger.LogError(ex, "Award {AwardId} ERP Purchase Order sync failed (attempt {RetryCount})", award.Id, award.ErpRetryCount);
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
