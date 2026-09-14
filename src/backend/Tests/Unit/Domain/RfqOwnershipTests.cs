// The domain guards behind tender ownership.
//
// The HTTP surface is proven separately. What is asserted here is the aggregate's own refusals, which is where
// they have to live so a future caller cannot route around them.
//
// An unowned tender is what every tender predating ownership looks like, and the fallback in the notification
// layer depends on that absence being honest rather than an empty identifier. The control is that given an owner,
// it keeps it.
//
// Reassigning to the same person is refused, because a row saying ownership changed from somebody to themselves
// is a false entry in an append-only trail, which is what the audit row this operation exists for would become.
//
// Reassignment is also how a tender with no owner gets its first one. The alternative would be a second
// operation for a case that is the same operation.
//
// The control for the terminal-state theory is that the refusal is about there being nothing left to own, not
// about the tender being late in its life.
//
//
// THE ASSIGNED APPROVER IS RECORDED SEPARATELY FROM WHOEVER DECIDED
//
// A nominated approver who is unavailable and the colleague who decides in their place are two different people,
// and a trail that keeps only the second cannot answer who was asked.
//
// The control is the normal case: there is no approval-routing rule to fall back on, so the absence is recorded
// rather than filled in.
//
// The setup walks the real transitions rather than setting the state directly, so the states reached are ones the
// machine actually admits.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

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
        CreateDraftRfq().OwnerUserId.Should().BeNull();
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

        var act = () => rfq.Reassign(second);
        act.Should().Throw<DomainException>().WithMessage("*already owns*");
    }

    [Fact]
    public void An_unowned_RFQ_can_be_claimed()
    {
        var rfq = CreateDraftRfq();
        var claimant = Guid.CreateVersion7();

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

        rfq.Approve(whoActuallyDecided);
        pass.AssignedApproverUserId.Should().Be(nominated);
        pass.ApproverUserId.Should().Be(whoActuallyDecided);
    }

    [Fact]
    public void A_review_pass_that_names_nobody_records_nobody()
    {
        var rfq = CreateReadyToSubmitRfq(Guid.CreateVersion7());

        rfq.SubmitForReview();

        rfq.Approvals.Single().AssignedApproverUserId.Should().BeNull();
    }

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
