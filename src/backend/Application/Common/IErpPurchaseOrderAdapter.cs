namespace MotsSupplierPortal.Application.Common;

/// <summary>FEAT-14.5/FR-AWD-005, BRULE-078/079: the ACL that would translate an awarded RFQ into a
/// real ERPNext Purchase Order (EPIC-23 scope, not built here) - narrower than IOutboxTransport
/// (which is fire-and-forget) because AwardErpSyncJob needs the actual PO reference back to write
/// onto the Award aggregate, not just a "dispatched" acknowledgement. Throws on failure (any
/// exception) rather than returning a result union - AwardErpSyncJob's own try/catch is the single
/// place that decides Synced vs Failed, matching OutboxDispatcher's own exception-based
/// success/failure shape.
///
/// <para><b>EPIC-13/FEAT-13.4 audit finding, addressed at the contract level:</b> AwardErpSyncJob
/// persists <c>ErpSyncStatus</c> only AFTER this call returns, via a single per-award
/// <c>SaveChangesAsync</c> - a crash or restart between a successful remote call and that commit
/// means the job's own recurring schedule will call this method again for the SAME award on its
/// next run (the row is still `ErpSyncStatus == Requested`). Reducing that window further on the
/// LOCAL side cannot make a REMOTE call idempotent - the standard, correct fix in a distributed
/// system is an idempotent remote endpoint, not fewer local retries. <paramref name="awardId"/> is
/// therefore the intended idempotency key: a real ERPNext adapter (EPIC-23) MUST treat a repeated
/// call with the same <paramref name="awardId"/> as "return the PO already created for this award"
/// rather than creating a second Purchase Order, exactly matching BRULE-078's own
/// "exactly-effectively-once" requirement. The stub cannot demonstrate this (it has no persistence
/// of its own to dedupe against) - stated here as the real adapter's obligation, not silently left
/// unaddressed.</para></summary>
public interface IErpPurchaseOrderAdapter
{
    Task<string> CreatePurchaseOrderAsync(Guid awardId, string rfqReferenceCode, CancellationToken ct = default);
}
