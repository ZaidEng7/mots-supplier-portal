using FluentAssertions;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>
/// A-7's domain guards. The HTTP surface is proven separately (Tests/Integration/RfqOwnershipTests);
/// what is asserted here is the aggregate's own refusals, which is where they have to live so a future
/// caller cannot route around them.
/// </summary>
public class RfqOwnershipTests
{
    private static readonly Guid OrgId = Guid.CreateVersion7();

    private static Rfq CreateDraftRfq(Guid? ownerUserId = null) => Rfq.Create(
        "RFQ-2026-000001", OrgId, "طلب اختبار", "Test RFQ", null, null, "SYP",
        publishAt: null,
        submissionOpensAt: DateTimeOffset.UtcNow.AddDays(1),
        submissionClosesAt: DateTimeOffset.UtcNow.AddDays(8),
        clarificationDeadlineAt: null, evaluationTargetDate: null,
        ownerUserId: ownerUserId);

    private static Rfq CreateReadyToSubmitRfq(Guid? ownerUserId = null)
    {
        var rfq = CreateDraftRfq(ownerUserId);
        rfq.AddItem("بند", "Item", null, null, "catering", 10m, "unit", isUnitPrice: true, isOptional: false);
        rfq.BindEvaluationTemplate(Guid.CreateVersion7(), 1, """{"criteria":[]}""");
        rfq.InviteSupplier(Guid.CreateVersion7());
        return rfq;
    }

    [Fact]
    public void An_RFQ_created_without_an_owner_has_none_rather_than_a_guessed_one()
    {
        // Every RFQ that predates A-7 looks like this, and the fallback in the notification layer
        // depends on it being an honest null rather than Guid.Empty.
        CreateDraftRfq().OwnerUserId.Should().BeNull();
        // The control: given an owner, it keeps it.
        var owner = Guid.CreateVersion7();
        CreateDraftRfq(owner).OwnerUserId.Should().Be(owner);
    }

    [Fact]
    public void Reassignment_moves_ownership_and_is_refused_when_it_would_change_nothing()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var rfq = CreateDraftRfq(first);

        rfq.Reassign(second);
        rfq.OwnerUserId.Should().Be(second);

        // A row saying ownership changed from a person to the same person is a false entry in an
        // append-only trail, which is what the audit row this method exists for would become.
        var act = () => rfq.Reassign(second);
        act.Should().Throw<DomainException>().WithMessage("*already owns*");
    }

    [Fact]
    public void An_unowned_RFQ_can_be_claimed()
    {
        var rfq = CreateDraftRfq();
        var claimant = Guid.CreateVersion7();

        // Reassignment is also how a legacy row gets its first owner - the alternative would be a
        // second operation for a case that is the same operation.
        rfq.Reassign(claimant);
        rfq.OwnerUserId.Should().Be(claimant);
    }

    [Theory]
    [InlineData(RfqState.Completed)]
    [InlineData(RfqState.Cancelled)]
    public void A_closed_RFQ_cannot_be_reassigned(RfqState terminal)
    {
        var rfq = CreateReadyToSubmitRfq(Guid.CreateVersion7());
        DriveTo(rfq, terminal);

        var act = () => rfq.Reassign(Guid.CreateVersion7());
        act.Should().Throw<DomainException>().WithMessage("*no action remains*");
    }

    [Fact]
    public void An_awarded_RFQ_is_still_reassignable_because_post_award_work_exists()
    {
        // The control for the theory above: the refusal is about there being nothing left to own, not
        // about the tender being late in its life.
        var rfq = CreateReadyToSubmitRfq(Guid.CreateVersion7());
        DriveTo(rfq, RfqState.Awarded);

        var newOwner = Guid.CreateVersion7();
        rfq.Reassign(newOwner);
        rfq.OwnerUserId.Should().Be(newOwner);
    }

    [Fact]
    public void The_review_pass_records_the_approver_it_was_assigned_to_separately_from_the_one_who_decided()
    {
        var nominated = Guid.CreateVersion7();
        var whoActuallyDecided = Guid.CreateVersion7();
        var rfq = CreateReadyToSubmitRfq(Guid.CreateVersion7());

        rfq.SubmitForReview(nominated);
        var pass = rfq.Approvals.Single();
        pass.AssignedApproverUserId.Should().Be(nominated);
        pass.ApproverUserId.Should().BeNull("nobody has decided it yet");

        // A nominated approver who is unavailable and the colleague who decides in their place are two
        // different people, and a trail that keeps only the second cannot answer who was asked.
        rfq.Approve(whoActuallyDecided);
        pass.AssignedApproverUserId.Should().Be(nominated);
        pass.ApproverUserId.Should().Be(whoActuallyDecided);
    }

    [Fact]
    public void A_review_pass_that_names_nobody_records_nobody()
    {
        // The control for the test above, and the normal case: there is no approval-routing rule to
        // fall back on (BRULE-072/074, OQ-004, T-075), so the absence is recorded rather than filled in.
        var rfq = CreateReadyToSubmitRfq(Guid.CreateVersion7());

        rfq.SubmitForReview();

        rfq.Approvals.Single().AssignedApproverUserId.Should().BeNull();
    }

    /// <summary>Walks the real transitions rather than setting State, so the states reached are ones
    /// the machine actually admits.</summary>
    private static void DriveTo(Rfq rfq, RfqState target)
    {
        if (target == RfqState.Cancelled)
        {
            rfq.Cancel("Superseded by a framework agreement.");
            return;
        }

        rfq.SubmitForReview();
        rfq.Approve(Guid.CreateVersion7());
        rfq.Publish();
        rfq.OpenSubmissionWindow();
        rfq.CloseSubmissionWindow(reason: null, isEarlyClose: false);
        rfq.OpenEvaluation();
        rfq.BeginShortlisting();
        rfq.RecordRecommendation();
        rfq.EnterAwardApproval();
        rfq.MarkAwarded();
        if (target == RfqState.Awarded) return;
        rfq.Complete();
    }
}
