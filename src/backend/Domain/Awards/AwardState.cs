namespace MotsSupplierPortal.Domain.Awards;

/// <summary>Canonical machine (BUSINESS-PROCESSES.md §6): "Recommended -&gt; PendingApproval ->
/// Approved | Rejected -&gt; Awarded -&gt; (Outbox -&gt; ERP PO)"; Rejected re-enters Recommended
/// on re-recommend.
///
/// <para><b>Awarded is terminal for this enum, deliberately not literally ErpPoRequested/
/// ErpPoSynced/ErpPoFailed as the mermaid diagram's own state names might suggest.</b> BRULE-077
/// is explicit: "Award is final within the portal upon Awarded, independent of ERP availability" -
/// if Award.State itself moved on to ErpPoRequested/ErpPoFailed, a still-failing ERP sync would
/// leave the award sitting in a non-terminal, ERP-shaped state forever, which is exactly the
/// "blocks on ERP" outcome BRULE-077 forbids. The ERP sub-flow is tracked instead by the separate,
/// independently-mutable <see cref="Award.ErpSyncStatus"/> field - see that property's own doc
/// comment for why it is excluded from the immutable award file (FEAT-14.7).</para></summary>
public enum AwardState
{
    Recommended,
    PendingApproval,
    Approved,
    Rejected,
    Awarded,
}

/// <summary>Award.ErpSyncStatus's own values - the ErpPoRequested/ErpPoSynced/ErpPoFailed half of
/// the mermaid diagram, tracked independently of AwardState (see that enum's own doc comment).</summary>
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
