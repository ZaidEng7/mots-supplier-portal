using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Domain.Awards;

/// <summary>The recommendation -&gt; approval -&gt; decision chain that concludes an RFQ
/// (docs/architecture/DOMAIN-MODEL.md §5.8), its own aggregate root (schema "award"), referenced by
/// RfqId - same "own bounded context, referenced by id" shape as Proposal/Evaluation.
///
/// <para><b>OQ-004, resolved:</b> confirmed back when EPIC-07 was built - single approver, final
/// decision, not the multi-level/amount-band hierarchy BUSINESS-RULES.md's BRULE-072/074 still
/// describe as `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. This build never creates more than
/// one active <see cref="Approval"/> per approval cycle and never checks an authority-limit band -
/// <see cref="Approvals"/> stays array-shaped only so a real multi-step chain later is a config/data
/// extension, not a schema migration, exactly matching RfqApproval's own precedent.</para>
///
/// <para><b>Immutability post-Awarded (FEAT-14.7/FR-AWD-008/BRULE-083), enforced structurally, not
/// by convention:</b> every mutating method below (<see cref="ReRecommend"/>, <see
/// cref="RouteForApproval"/>, <see cref="Approve"/>, <see cref="Reject"/>, <see
/// cref="ExecuteAward"/>) guards on a PRE-Awarded state and throws otherwise - once <see
/// cref="State"/> is Awarded, none of them has a reachable success path, because Awarded is not one
/// of any of their accepted "from" states. There is no separate "lock" flag to forget to check; the
/// state machine itself is the lock. The one exception is the ERP sync fields
/// (<see cref="ErpSyncStatus"/>, <see cref="ExternalPurchaseOrderRef"/>, <see cref="ErpSyncedAt"/>,
/// <see cref="ErpRetryCount"/>) - these are deliberately NOT part of the immutable award file: they
/// track an ASYNCHRONOUS INFRASTRUCTURE SYNC outcome, not a procurement decision, and BRULE-077/079
/// require them to keep changing after Awarded (Requested -&gt; Synced|Failed -&gt; retry) for the
/// award to ever reach RFQ Completion. The decision itself - Recommendation, Approvals,
/// AwardDecision fields, ComparisonSnapshotJson - never changes again.</para></summary>
public sealed class Award
{
    private readonly List<Approval> _approvals = [];

    public Guid Id { get; private init; }
    public Guid RfqId { get; private init; }
    public AwardState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public uint RowVersion { get; private set; }

    // Recommendation - single owned entity per DOMAIN-MODEL.md's own "Award 1 *-- 1 Recommendation"
    // cardinality, overwritten (with an incrementing revision counter) on re-recommend rather than
    // accumulating a list nothing in the docs specifies the shape of.
    public Guid WinningProposalId { get; private set; }
    public string JustificationAr { get; private set; } = null!;
    public string JustificationEn { get; private set; } = null!;
    public Guid RecommendedByUserId { get; private set; }
    public DateTimeOffset RecommendedAt { get; private set; }
    public int RecommendationRevision { get; private set; }

    // AwardDecision - set once, at ExecuteAward, never again.
    public DateTimeOffset? AwardedAt { get; private set; }
    public string? ComparisonSnapshotJson { get; private set; }

    // ERP sync sub-flow - mutable post-Awarded, deliberately excluded from the immutable award
    // file; see this class's own doc comment.
    public ErpSyncStatus ErpSyncStatus { get; private set; } = ErpSyncStatus.NotRequested;
    public string? ExternalPurchaseOrderRef { get; private set; }
    public DateTimeOffset? ErpSyncedAt { get; private set; }
    public int ErpRetryCount { get; private set; }

    public IReadOnlyList<Approval> Approvals => _approvals;

    private Award() { }

    /// <summary>FEAT-14.1/FR-AWD-001, BRULE-071: "may be recorded only after evaluation is
    /// Finalized and the recommended proposal passes all thresholds" - both cross-aggregate facts
    /// (Evaluation lives elsewhere), resolved by the handler before calling this, same split as
    /// every other cross-aggregate guard in this codebase.</summary>
    public static Award Recommend(Guid rfqId, Guid winningProposalId, string justificationAr, string justificationEn, Guid recommendedByUserId)
    {
        if (string.IsNullOrWhiteSpace(justificationAr) || string.IsNullOrWhiteSpace(justificationEn))
        {
            throw new DomainException("A justification (Arabic and English) is required to recommend an award.");
        }
        return new Award
        {
            Id = Guid.CreateVersion7(),
            RfqId = rfqId,
            State = AwardState.Recommended,
            WinningProposalId = winningProposalId,
            JustificationAr = justificationAr,
            JustificationEn = justificationEn,
            RecommendedByUserId = recommendedByUserId,
            RecommendedAt = DateTimeOffset.UtcNow,
            RecommendationRevision = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>BUSINESS-PROCESSES.md §6.1 "Rejected -&gt; Recommended: Re-recommend ... New/again
    /// justification ... New recommendation revision". Approval history is not cleared - prior
    /// Approval rows (including the rejection) stay in <see cref="Approvals"/> as the audit record
    /// of the earlier cycle; RouteForApproval() adds a fresh one for this cycle.</summary>
    public void ReRecommend(Guid winningProposalId, string justificationAr, string justificationEn, Guid recommendedByUserId)
    {
        if (State != AwardState.Rejected)
        {
            throw new DomainException($"Cannot re-recommend from state '{State}'; only 'Rejected' is valid.");
        }
        if (string.IsNullOrWhiteSpace(justificationAr) || string.IsNullOrWhiteSpace(justificationEn))
        {
            throw new DomainException("A justification (Arabic and English) is required to recommend an award.");
        }
        WinningProposalId = winningProposalId;
        JustificationAr = justificationAr;
        JustificationEn = justificationEn;
        RecommendedByUserId = recommendedByUserId;
        RecommendedAt = DateTimeOffset.UtcNow;
        RecommendationRevision++;
        State = AwardState.Recommended;
    }

    public void RouteForApproval()
    {
        if (State != AwardState.Recommended)
        {
            throw new DomainException($"Cannot route for approval from state '{State}'; only 'Recommended' is valid.");
        }
        _approvals.Add(new Approval { Id = Guid.CreateVersion7(), AwardId = Id, StepNo = 1 });
        State = AwardState.PendingApproval;
    }

    private Approval ActiveApproval() =>
        _approvals.LastOrDefault(a => a.Decision is null)
        ?? throw new DomainException("No pending approval step to decide.");

    /// <summary>BRULE-073, defense in depth: the primary enforcement point for segregation of
    /// duties is the API handler (BUSINESS-PROCESSES.md §6.1's own "Approver ≠ recommender"
    /// column), but the check is repeated here too since RecommendedByUserId is already on this
    /// aggregate and a second, cheap guard against the exact same invariant costs nothing.</summary>
    public void Approve(Guid approverUserId)
    {
        if (State != AwardState.PendingApproval)
        {
            throw new DomainException($"Cannot approve from state '{State}'; only 'PendingApproval' is valid.");
        }
        if (approverUserId == RecommendedByUserId)
        {
            throw new DomainException("Segregation of duties: the approver must differ from the recommender.");
        }
        var approval = ActiveApproval();
        approval.ApproverUserId = approverUserId;
        approval.Decision = ApprovalDecision.Approved;
        approval.DecidedAt = DateTimeOffset.UtcNow;
        State = AwardState.Approved;
    }

    public void Reject(Guid approverUserId, string reason)
    {
        if (State != AwardState.PendingApproval)
        {
            throw new DomainException($"Cannot reject from state '{State}'; only 'PendingApproval' is valid.");
        }
        if (approverUserId == RecommendedByUserId)
        {
            throw new DomainException("Segregation of duties: the approver must differ from the recommender.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A rejection reason is required.");
        }
        var approval = ActiveApproval();
        approval.ApproverUserId = approverUserId;
        approval.Decision = ApprovalDecision.Rejected;
        approval.Comment = reason;
        approval.DecidedAt = DateTimeOffset.UtcNow;
        State = AwardState.Rejected;
    }

    /// <summary>FEAT-14.4/FEAT-14.5/FR-AWD-004/005: "execute award" - sets the AwardDecision fields
    /// and immediately requests ERP sync (BUSINESS-PROCESSES.md §6's own "Awarded -&gt;
    /// ErpPoRequested: Outbox emit" is drawn as the very next step, not a separate later action).
    /// <paramref name="comparisonSnapshotJson"/> is the frozen EPIC-12 comparison view at the moment
    /// of award (FEAT-14.7) - captured here, never re-queried live once Awarded.</summary>
    public void ExecuteAward(string comparisonSnapshotJson)
    {
        if (State != AwardState.Approved)
        {
            throw new DomainException($"Cannot execute award from state '{State}'; only 'Approved' is valid.");
        }
        State = AwardState.Awarded;
        AwardedAt = DateTimeOffset.UtcNow;
        ComparisonSnapshotJson = comparisonSnapshotJson;
        ErpSyncStatus = ErpSyncStatus.Requested;
    }

    /// <summary>BRULE-078/079: called by AwardErpSyncJob once the (stub) ERP adapter acknowledges.
    /// Never regresses AwardState - see this class's own doc comment on why the ERP sub-flow is a
    /// separate field.</summary>
    public void MarkErpSynced(string externalPurchaseOrderRef)
    {
        if (ErpSyncStatus is not (ErpSyncStatus.Requested or ErpSyncStatus.Failed))
        {
            throw new DomainException($"Cannot mark ERP synced from status '{ErpSyncStatus}'.");
        }
        if (string.IsNullOrWhiteSpace(externalPurchaseOrderRef))
        {
            throw new DomainException("An external purchase order reference is required.");
        }
        ExternalPurchaseOrderRef = externalPurchaseOrderRef;
        ErpSyncStatus = ErpSyncStatus.Synced;
        ErpSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>BRULE-077: the award itself is untouched by this - AwardState stays Awarded,
    /// final, regardless of how many times this is called.</summary>
    public void MarkErpFailed()
    {
        if (ErpSyncStatus != ErpSyncStatus.Requested)
        {
            throw new DomainException($"Cannot mark ERP failed from status '{ErpSyncStatus}'.");
        }
        ErpSyncStatus = ErpSyncStatus.Failed;
        ErpRetryCount++;
    }

    /// <summary>BUSINESS-PROCESSES.md §6.1 "ErpPoFailed -&gt; ErpPoRequested: Retry ...
    /// system,system_admin / integration.retry".</summary>
    public void RetryErpSync()
    {
        if (ErpSyncStatus != ErpSyncStatus.Failed)
        {
            throw new DomainException($"Cannot retry ERP sync from status '{ErpSyncStatus}'.");
        }
        ErpSyncStatus = ErpSyncStatus.Requested;
    }
}
