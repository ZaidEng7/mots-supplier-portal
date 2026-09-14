// The states an award moves through:
//
//   Recommended → PendingApproval → Approved → Awarded
//   PendingApproval → Rejected → Recommended  (the manager re-recommends)
//
// Awarded is final. It stays final even if the ministry's finance system never
// acknowledges the purchase order, because the award is a decision the portal has made and
// it must not hang on another system being available.
//
// That is why the finance handshake is tracked separately, in ErpSyncStatus:
//
//   NotRequested → Requested → Synced
//                  Requested → Failed → Requested  (retry)
//
// If the award's own state moved on to an ERP-shaped state, a finance system that stayed
// down would leave the award stuck in a non-final state forever. Two fields keep the
// decision and the delivery of that decision apart.
//
// ApprovalDecision is what one approver said on one step.

namespace MotsSupplierPortal.Domain.Awards;

public enum AwardState
{
    Recommended,
    PendingApproval,
    Approved,
    Rejected,
    Awarded,
}

public enum ErpSyncStatus
{
    NotRequested,
    Requested,
    Synced,
    Failed,
}

public enum ApprovalDecision
{
    Approved,
    Rejected,
}
