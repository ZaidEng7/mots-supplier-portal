// The decision that concludes a tender: a manager recommends a winner, a different manager
// approves it, and the award is issued.
//
//   Recommend        a manager names the winning bid and writes a justification
//   RouteForApproval  sends it to an approver and opens an approval step
//   Approve / Reject  the approver decides; a rejection sends it back to be re-recommended
//   ExecuteAward      issues the award and freezes the comparison as it stood
//
// The approver must not be the recommender. That rule is enforced in the request handler
// and again here, because this object already knows who recommended and a second cheap
// check costs nothing.
//
// Once the state is Awarded the decision can never change. There is no lock flag to forget
// to check: every method above accepts only a state before Awarded, so once it is Awarded
// none of them has a path that succeeds. The state machine is the lock.
//
// The exception is the finance handshake - ErpSyncStatus, ExternalPurchaseOrderRef,
// ErpSyncedAt, ErpRetryCount - which must keep moving after the award is final, because a
// tender only reaches Completed once the purchase order is acknowledged. Those fields
// track an infrastructure outcome, not a procurement decision, which is why they are not
// part of the frozen award file.
//
// Re-recommending after a rejection overwrites the recommendation and increments its
// revision number. Past approval rows are kept, including the rejection, as the record of
// the earlier cycle.
//
// ComparisonSnapshotJson is the comparison table as it stood at the moment of award. It is
// captured once and never re-queried, so the file shows what the decision was actually
// made on.

namespace MotsSupplierPortal.Domain.Awards;

using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class Award : IVersionedAggregate
{
    private readonly List<Approval> _approvals = [];

    public Guid Id { get; private init; }
    public Guid RfqId { get; private init; }
    public AwardState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public uint RowVersion { get; private set; }

    public Guid WinningProposalId { get; private set; }
    public string JustificationAr { get; private set; } = null!;
    public string JustificationEn { get; private set; } = null!;
    public Guid RecommendedByUserId { get; private set; }
    public DateTimeOffset RecommendedAt { get; private set; }
    public int RecommendationRevision { get; private set; }

    public DateTimeOffset? AwardedAt { get; private set; }
    public string? ComparisonSnapshotJson { get; private set; }

    public ErpSyncStatus ErpSyncStatus { get; private set; } = ErpSyncStatus.NotRequested;
    public string? ExternalPurchaseOrderRef { get; private set; }
    public DateTimeOffset? ErpSyncedAt { get; private set; }
    public int ErpRetryCount { get; private set; }

    public IReadOnlyList<Approval> Approvals => _approvals;

    private Award() { }

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

    public void MarkErpFailed()
    {
        if (ErpSyncStatus != ErpSyncStatus.Requested)
        {
            throw new DomainException($"Cannot mark ERP failed from status '{ErpSyncStatus}'.");
        }
        ErpSyncStatus = ErpSyncStatus.Failed;
        ErpRetryCount++;
    }

    public void RetryErpSync()
    {
        if (ErpSyncStatus != ErpSyncStatus.Failed)
        {
            throw new DomainException($"Cannot retry ERP sync from status '{ErpSyncStatus}'.");
        }
        ErpSyncStatus = ErpSyncStatus.Requested;
    }
}
