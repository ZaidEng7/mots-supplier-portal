namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>
/// SupplierDocument sub-lifecycle (docs/architecture/DOMAIN-MODEL.md §5.3): a row only exists once
/// a file has actually been uploaded - "Required but not yet uploaded" is a computed UI status
/// (DocumentType.IsRequired with no matching row), not a persisted state.
/// PendingScan -> Uploaded | ScanRejected (docs/security/SECURITY-ARCHITECTURE.md §4.1 quarantine
/// pipeline) -> UnderReview -> Approved | Rejected; time-based Approved -> ExpiringSoon -> Expired.
/// </summary>
public enum DocumentState
{
    PendingScan,
    ScanRejected,
    Uploaded,
    UnderReview,
    Approved,
    Rejected,
    ExpiringSoon,
    Expired,
}
