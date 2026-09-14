// The seam that would turn an awarded tender into a real purchase order in the ministry's finance system.
// That integration is not built here.
//
// It is narrower than the outbox transport, which sends and forgets, because the sync job needs the
// purchase-order reference back to write onto the award.
//
// It throws on failure rather than returning an outcome. The sync job's own handling is the single place
// that decides whether a sync succeeded or failed, which matches how the outbox dispatcher already works.
//
//
// WHY THE AWARD'S IDENTIFIER IS THE IDEMPOTENCY KEY
//
// The sync job records the outcome only after this call returns, in one save per award. A crash or a
// restart between a successful remote call and that save means the job's own schedule will call this again
// for the same award on its next run, because the record still says a sync was requested.
//
// Narrowing that window on our side cannot make a remote call idempotent. The correct fix in a distributed
// system is an idempotent remote endpoint rather than fewer local retries.
//
// So a real implementation must treat a repeated call carrying the same award identifier as "return the
// purchase order already created for this award" rather than creating a second one.

namespace MotsSupplierPortal.Application.Common;

public interface IErpPurchaseOrderAdapter
{
    Task<string> CreatePurchaseOrderAsync(Guid awardId, string rfqReferenceCode, CancellationToken ct = default);
}
