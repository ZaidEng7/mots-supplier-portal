using FluentAssertions;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>FEAT-14.1..14.5. State list/transitions verified directly against
/// BUSINESS-PROCESSES.md §6 - see Award.cs's own doc comments for the exact quoted rows this
/// exercises. Segregation of duties (BRULE-073) and post-Awarded immutability (BRULE-083) are this
/// file's centerpieces.</summary>
public class AwardTests
{
    private static readonly Guid RfqId = Guid.CreateVersion7();
    private static readonly Guid ProposalId = Guid.CreateVersion7();
    private static readonly Guid RecommenderId = Guid.CreateVersion7();
    private static readonly Guid ApproverId = Guid.CreateVersion7();

    private static Award CreateRecommended() =>
        Award.Recommend(RfqId, ProposalId, "مبرر", "Justification", RecommenderId);

    [Fact]
    public void New_award_starts_recommended()
    {
        CreateRecommended().State.Should().Be(AwardState.Recommended);
    }

    [Fact]
    public void Recommend_requires_a_justification_in_both_languages()
    {
        Action act = () => Award.Recommend(RfqId, ProposalId, "", "Justification", RecommenderId);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void RouteForApproval_creates_a_single_step_1_approval_and_transitions_to_pending()
    {
        var award = CreateRecommended();
        award.RouteForApproval();

        award.State.Should().Be(AwardState.PendingApproval);
        award.Approvals.Should().ContainSingle();
        award.Approvals[0].StepNo.Should().Be(1);
        award.Approvals[0].Decision.Should().BeNull();
    }

    // ---- Segregation of duties (BRULE-073) - the centerpiece ----

    [Fact]
    public void Approve_refuses_the_recommender_approving_their_own_recommendation()
    {
        var award = CreateRecommended();
        award.RouteForApproval();

        Action act = () => award.Approve(RecommenderId);

        act.Should().Throw<DomainException>().WithMessage("*Segregation of duties*");
        award.State.Should().Be(AwardState.PendingApproval, "a refused self-approval must not change state");
    }

    [Fact]
    public void Reject_also_refuses_the_recommender_rejecting_their_own_recommendation()
    {
        var award = CreateRecommended();
        award.RouteForApproval();

        Action act = () => award.Reject(RecommenderId, "changed my mind");

        act.Should().Throw<DomainException>().WithMessage("*Segregation of duties*");
    }

    [Fact]
    public void Approve_by_a_different_user_succeeds()
    {
        var award = CreateRecommended();
        award.RouteForApproval();

        award.Approve(ApproverId);

        award.State.Should().Be(AwardState.Approved);
        award.Approvals[0].ApproverUserId.Should().Be(ApproverId);
        award.Approvals[0].Decision.Should().Be(ApprovalDecision.Approved);
    }

    [Fact]
    public void Reject_requires_a_reason_and_returns_to_recommended_via_ReRecommend()
    {
        var award = CreateRecommended();
        award.RouteForApproval();

        Action noReason = () => award.Reject(ApproverId, "");
        noReason.Should().Throw<DomainException>();

        award.Reject(ApproverId, "price too high");
        award.State.Should().Be(AwardState.Rejected);

        award.ReRecommend(ProposalId, "مبرر جديد", "New justification", RecommenderId);
        award.State.Should().Be(AwardState.Recommended);
        award.RecommendationRevision.Should().Be(2);
        award.Approvals.Should().ContainSingle("the rejected approval stays as history; re-recommend does not clear it");
    }

    [Fact]
    public void ExecuteAward_requires_Approved_and_captures_the_comparison_snapshot()
    {
        var award = CreateRecommended();
        award.RouteForApproval();
        award.Approve(ApproverId);

        award.ExecuteAward("{\"snapshot\":true}");

        award.State.Should().Be(AwardState.Awarded);
        award.AwardedAt.Should().NotBeNull();
        award.ComparisonSnapshotJson.Should().Be("{\"snapshot\":true}");
        award.ErpSyncStatus.Should().Be(ErpSyncStatus.Requested);
    }

    // ---- Post-Awarded immutability (BRULE-083/FEAT-14.7) - structural, not conventional ----

    /// <summary>Revert-to-red: every mutating method attempted against an Awarded instance, proving
    /// each one's own guard - not a separate lock flag - is what makes the award file immutable.</summary>
    [Fact]
    public void Every_mutating_method_refuses_once_Awarded()
    {
        var award = CreateRecommended();
        award.RouteForApproval();
        award.Approve(ApproverId);
        award.ExecuteAward("{}");
        award.State.Should().Be(AwardState.Awarded);

        ((Action)(() => award.ReRecommend(ProposalId, "x", "y", RecommenderId))).Should().Throw<DomainException>();
        ((Action)(() => award.RouteForApproval())).Should().Throw<DomainException>();
        ((Action)(() => award.Approve(ApproverId))).Should().Throw<DomainException>();
        ((Action)(() => award.Reject(ApproverId, "reason"))).Should().Throw<DomainException>();
        ((Action)(() => award.ExecuteAward("{}"))).Should().Throw<DomainException>();

        // The immutable core is untouched by any of the above.
        award.WinningProposalId.Should().Be(ProposalId);
        award.ComparisonSnapshotJson.Should().Be("{}");
        award.State.Should().Be(AwardState.Awarded);
    }

    // ---- ERP sync sub-flow (BRULE-077/078/079) ----

    [Fact]
    public void ERP_sync_status_can_progress_and_regress_independently_of_AwardState()
    {
        var award = CreateRecommended();
        award.RouteForApproval();
        award.Approve(ApproverId);
        award.ExecuteAward("{}");
        award.ErpSyncStatus.Should().Be(ErpSyncStatus.Requested);

        award.MarkErpFailed();
        award.ErpSyncStatus.Should().Be(ErpSyncStatus.Failed);
        award.ErpRetryCount.Should().Be(1);
        award.State.Should().Be(AwardState.Awarded, "AwardState must never regress when ERP sync fails");

        award.RetryErpSync();
        award.ErpSyncStatus.Should().Be(ErpSyncStatus.Requested);

        award.MarkErpSynced("PO-000123");
        award.ErpSyncStatus.Should().Be(ErpSyncStatus.Synced);
        award.ExternalPurchaseOrderRef.Should().Be("PO-000123");
        award.State.Should().Be(AwardState.Awarded);
    }
}
