namespace MotsSupplierPortal.Application.Common;

/// <summary>FEAT-14.5/FR-AWD-005, BRULE-078/079: the ACL that would translate an awarded RFQ into a
/// real ERPNext Purchase Order (EPIC-23 scope, not built here) - narrower than IOutboxTransport
/// (which is fire-and-forget) because AwardErpSyncJob needs the actual PO reference back to write
/// onto the Award aggregate, not just a "dispatched" acknowledgement. Throws on failure (any
/// exception) rather than returning a result union - AwardErpSyncJob's own try/catch is the single
/// place that decides Synced vs Failed, matching OutboxDispatcher's own exception-based
/// success/failure shape.</summary>
public interface IErpPurchaseOrderAdapter
{
    Task<string> CreatePurchaseOrderAsync(Guid awardId, string rfqReferenceCode, CancellationToken ct = default);
}
