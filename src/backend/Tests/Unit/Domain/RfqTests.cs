using FluentAssertions;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>FEAT-07.1..07.10. State list/transitions verified directly against
/// docs/product/BUSINESS-PROCESSES.md §3.1 - see Rfq.cs's own doc comments for the exact quoted
/// transition rows this exercises.</summary>
public class RfqTests
{
    private static readonly Guid OrgId = Guid.CreateVersion7();
    private static readonly Guid ApproverId = Guid.CreateVersion7();

    private static Rfq CreateDraftRfq(DateTimeOffset? opensAt = null, DateTimeOffset? closesAt = null) => Rfq.Create(
        "RFQ-2026-000001", OrgId, "طلب اختبار", "Test RFQ", null, null, "SYP",
        publishAt: null,
        submissionOpensAt: opensAt ?? DateTimeOffset.UtcNow.AddDays(1),
        submissionClosesAt: closesAt ?? DateTimeOffset.UtcNow.AddDays(8),
        clarificationDeadlineAt: null, evaluationTargetDate: null);

    private static Rfq CreateReadyToSubmitRfq()
    {
        var rfq = CreateDraftRfq();
        rfq.AddItem("بند", "Item", null, null, "catering", 10m, "unit", isUnitPrice: true, isOptional: false);
        rfq.BindEvaluationTemplate(Guid.CreateVersion7(), 1, """{"criteria":[]}""");
        rfq.InviteSupplier(Guid.CreateVersion7());
        return rfq;
    }

    [Fact]
    public void Create_rejects_a_close_date_not_after_the_open_date()
    {
        var now = DateTimeOffset.UtcNow;
        var act = () => Rfq.Create("RFQ-2026-000002", OrgId, "ت", "T", null, null, "SYP",
            null, now.AddDays(5), now.AddDays(5), null, null);

        act.Should().Throw<DomainException>().WithMessage("*strictly after*");
    }

    [Fact]
    public void New_rfq_starts_in_draft()
    {
        CreateDraftRfq().State.Should().Be(RfqState.Draft);
    }

    [Fact]
    public void AddItem_is_rejected_once_the_rfq_leaves_draft()
    {
        var rfq = CreateReadyToSubmitRfq();
        rfq.SubmitForReview();

        var act = () => rfq.AddItem("late", "late", null, null, "catering", 1m, "unit", false, false);

        act.Should().Throw<DomainException>().WithMessage("*only 'Draft' allows edits*");
    }

    [Fact]
    public void SubmitForReview_requires_at_least_one_item()
    {
        var rfq = CreateDraftRfq();
        rfq.BindEvaluationTemplate(Guid.CreateVersion7(), 1, "{}");

        var act = () => rfq.SubmitForReview();

        act.Should().Throw<DomainException>().WithMessage("*at least one RFQ item*");
    }

    [Fact]
    public void SubmitForReview_requires_a_bound_evaluation_template()
    {
        var rfq = CreateDraftRfq();
        rfq.AddItem("بند", "Item", null, null, "catering", 1m, "unit", true, false);

        var act = () => rfq.SubmitForReview();

        act.Should().Throw<DomainException>().WithMessage("*evaluation template must be bound*");
    }

    [Fact]
    public void SubmitForReview_requires_submission_dates_in_the_future()
    {
        var rfq = CreateDraftRfq(
            opensAt: DateTimeOffset.UtcNow.AddDays(-2),
            closesAt: DateTimeOffset.UtcNow.AddDays(-1));
        rfq.AddItem("بند", "Item", null, null, "catering", 1m, "unit", true, false);
        rfq.BindEvaluationTemplate(Guid.CreateVersion7(), 1, "{}");

        var act = () => rfq.SubmitForReview();

        act.Should().Throw<DomainException>().WithMessage("*future*");
    }

    [Fact]
    public void SubmitForReview_requires_at_least_one_invited_candidate_supplier()
    {
        var rfq = CreateDraftRfq();
        rfq.AddItem("بند", "Item", null, null, "catering", 1m, "unit", true, false);
        rfq.BindEvaluationTemplate(Guid.CreateVersion7(), 1, "{}");

        var act = () => rfq.SubmitForReview();

        act.Should().Throw<DomainException>().WithMessage("*at least one candidate supplier must be invited*");
    }

    [Fact]
    public void InviteSupplier_adds_an_invited_status_invitation()
    {
        var rfq = CreateDraftRfq();
        var supplierId = Guid.CreateVersion7();

        var invitation = rfq.InviteSupplier(supplierId);

        invitation.Status.Should().Be(InvitationStatus.Invited);
        rfq.Invitations.Should().ContainSingle(i => i.SupplierId == supplierId && i.Status == InvitationStatus.Invited);
    }

    [Fact]
    public void InviteSupplier_rejects_a_duplicate_invitation_to_the_same_supplier()
    {
        var rfq = CreateDraftRfq();
        var supplierId = Guid.CreateVersion7();
        rfq.InviteSupplier(supplierId);

        var act = () => rfq.InviteSupplier(supplierId);

        act.Should().Throw<DomainException>().WithMessage("*already been invited*");
    }

    [Fact]
    public void InviteSupplier_is_rejected_once_submission_has_closed()
    {
        var rfq = CreateReadyToSubmitRfq();
        AdvanceTo(rfq, RfqState.SubmissionClosed);

        var act = () => rfq.InviteSupplier(Guid.CreateVersion7());

        act.Should().Throw<DomainException>().WithMessage("*only allowed up to and including 'SubmissionOpen'*");
    }

    [Fact]
    public void InviteSupplier_is_allowed_while_submission_is_open_late_invite()
    {
        var rfq = CreateReadyToSubmitRfq();
        AdvanceTo(rfq, RfqState.SubmissionOpen);

        var act = () => rfq.InviteSupplier(Guid.CreateVersion7());

        act.Should().NotThrow();
    }

    [Fact]
    public void MarkInvitationViewed_moves_invited_to_viewed_once()
    {
        var rfq = CreateDraftRfq();
        var supplierId = Guid.CreateVersion7();
        rfq.InviteSupplier(supplierId);

        rfq.MarkInvitationViewed(supplierId);

        var invitation = rfq.Invitations.Single();
        invitation.Status.Should().Be(InvitationStatus.Viewed);
        invitation.ViewedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkInvitationViewed_does_not_regress_a_later_status_back_to_viewed()
    {
        var rfq = CreateDraftRfq();
        var supplierId = Guid.CreateVersion7();
        rfq.InviteSupplier(supplierId);
        rfq.DeclineInvitation(supplierId, "not this time");

        rfq.MarkInvitationViewed(supplierId);

        rfq.Invitations.Single().Status.Should().Be(InvitationStatus.Declined);
    }

    [Fact]
    public void DeclineInvitation_sets_declined_status_with_optional_reason()
    {
        var rfq = CreateDraftRfq();
        var supplierId = Guid.CreateVersion7();
        rfq.InviteSupplier(supplierId);

        rfq.DeclineInvitation(supplierId, "Capacity constraints");

        var invitation = rfq.Invitations.Single();
        invitation.Status.Should().Be(InvitationStatus.Declined);
        invitation.DeclineReason.Should().Be("Capacity constraints");
        invitation.RespondedAt.Should().NotBeNull();
    }

    [Fact]
    public void DeclineInvitation_rejects_a_supplier_with_no_invitation()
    {
        var rfq = CreateDraftRfq();

        var act = () => rfq.DeclineInvitation(Guid.CreateVersion7(), null);

        act.Should().Throw<DomainException>().WithMessage("*no invitation*");
    }

    [Fact]
    public void SubmitForReview_moves_draft_to_internal_review_and_creates_a_pending_approval_step()
    {
        var rfq = CreateReadyToSubmitRfq();

        rfq.SubmitForReview();

        rfq.State.Should().Be(RfqState.InternalReview);
        rfq.Approvals.Should().ContainSingle(a => a.StepNo == 1 && a.Decision == null);
    }

    [Fact]
    public void ReturnForEdits_moves_internal_review_back_to_draft_and_records_the_rejection()
    {
        var rfq = CreateReadyToSubmitRfq();
        rfq.SubmitForReview();

        rfq.ReturnForEdits(ApproverId, "Missing pricing detail");

        rfq.State.Should().Be(RfqState.Draft);
        var step = rfq.Approvals.Single(a => a.StepNo == 1);
        step.Decision.Should().Be(RfqApprovalDecision.Rejected);
        step.Comment.Should().Be("Missing pricing detail");
        step.ApproverUserId.Should().Be(ApproverId);
    }

    [Fact]
    public void ReturnForEdits_requires_comments()
    {
        var rfq = CreateReadyToSubmitRfq();
        rfq.SubmitForReview();

        var act = () => rfq.ReturnForEdits(ApproverId, "");

        act.Should().Throw<DomainException>().WithMessage("*Comments are required*");
    }

    [Fact]
    public void A_second_review_pass_after_return_creates_a_new_pending_approval_step()
    {
        var rfq = CreateReadyToSubmitRfq();
        rfq.SubmitForReview();
        rfq.ReturnForEdits(ApproverId, "fix it");
        rfq.SubmitForReview();

        rfq.Approvals.Should().HaveCount(2, "the array grows per review pass rather than overwriting the prior decision (OQ-004 interim shape)");
        rfq.Approvals.Should().Contain(a => a.StepNo == 1 && a.Decision == RfqApprovalDecision.Rejected);
        rfq.Approvals.Should().Contain(a => a.StepNo == 2 && a.Decision == null);
    }

    [Fact]
    public void Approve_moves_internal_review_to_approved_and_resolves_the_pending_step()
    {
        var rfq = CreateReadyToSubmitRfq();
        rfq.SubmitForReview();

        rfq.Approve(ApproverId);

        rfq.State.Should().Be(RfqState.Approved);
        var step = rfq.Approvals.Single();
        step.Decision.Should().Be(RfqApprovalDecision.Approved);
        step.ApproverUserId.Should().Be(ApproverId);
        step.DecidedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(RfqState.Draft)]
    [InlineData(RfqState.Approved)]
    public void Approve_is_rejected_from_any_state_other_than_internal_review(RfqState notInReview)
    {
        var rfq = CreateReadyToSubmitRfq();
        if (notInReview == RfqState.Approved)
        {
            rfq.SubmitForReview();
            rfq.Approve(ApproverId);
        }

        var act = () => rfq.Approve(ApproverId);

        act.Should().Throw<DomainException>().WithMessage("*only 'InternalReview' is valid*");
    }

    [Fact]
    public void Publish_moves_approved_to_published()
    {
        var rfq = CreateReadyToSubmitRfq();
        rfq.SubmitForReview();
        rfq.Approve(ApproverId);

        rfq.Publish();

        rfq.State.Should().Be(RfqState.Published);
    }

    [Fact]
    public void Publish_is_rejected_when_not_approved()
    {
        var rfq = CreateReadyToSubmitRfq();

        var act = () => rfq.Publish();

        act.Should().Throw<DomainException>().WithMessage("*only 'Approved' is valid*");
    }

    [Fact]
    public void OpenSubmissionWindow_moves_published_to_submission_open()
    {
        var rfq = CreateReadyToSubmitRfq();
        rfq.SubmitForReview();
        rfq.Approve(ApproverId);
        rfq.Publish();

        rfq.OpenSubmissionWindow();

        rfq.State.Should().Be(RfqState.SubmissionOpen);
    }

    [Fact]
    public void CloseSubmissionWindow_requires_a_reason_for_a_manual_early_close()
    {
        var rfq = CreateReadyToSubmitRfq();
        rfq.SubmitForReview();
        rfq.Approve(ApproverId);
        rfq.Publish();
        rfq.OpenSubmissionWindow();

        var act = () => rfq.CloseSubmissionWindow(reason: null, isEarlyClose: true);

        act.Should().Throw<DomainException>().WithMessage("*reason is required*");
    }

    [Fact]
    public void CloseSubmissionWindow_does_not_require_a_reason_for_a_scheduled_close()
    {
        var rfq = CreateReadyToSubmitRfq();
        rfq.SubmitForReview();
        rfq.Approve(ApproverId);
        rfq.Publish();
        rfq.OpenSubmissionWindow();

        var act = () => rfq.CloseSubmissionWindow(reason: null, isEarlyClose: false);

        act.Should().NotThrow();
        rfq.State.Should().Be(RfqState.SubmissionClosed);
    }

    [Theory]
    [InlineData(RfqState.Draft)]
    [InlineData(RfqState.InternalReview)]
    [InlineData(RfqState.Approved)]
    [InlineData(RfqState.Published)]
    [InlineData(RfqState.SubmissionOpen)]
    [InlineData(RfqState.SubmissionClosed)]
    public void Cancel_is_allowed_from_every_pre_awarded_state(RfqState targetState)
    {
        var rfq = CreateReadyToSubmitRfq();
        AdvanceTo(rfq, targetState);

        var act = () => rfq.Cancel("Budget withdrawn");

        act.Should().NotThrow();
        rfq.State.Should().Be(RfqState.Cancelled);
        rfq.CancelReason.Should().Be("Budget withdrawn");
    }

    [Fact]
    public void Cancel_requires_a_reason()
    {
        var rfq = CreateReadyToSubmitRfq();

        var act = () => rfq.Cancel("");

        act.Should().Throw<DomainException>().WithMessage("*reason is required*");
    }

    [Fact]
    public void Cancel_is_terminal_and_cannot_be_cancelled_again()
    {
        var rfq = CreateReadyToSubmitRfq();
        rfq.Cancel("first reason");

        var act = () => rfq.Cancel("second reason");

        act.Should().Throw<DomainException>().WithMessage("*only allowed pre-Awarded*");
    }

    [Fact]
    public void RemoveItem_renumbers_remaining_items_to_a_dense_sequence()
    {
        var rfq = CreateDraftRfq();
        var first = rfq.AddItem("1", "1", null, null, "catering", 1m, "unit", false, false);
        rfq.AddItem("2", "2", null, null, "catering", 1m, "unit", false, false);
        var third = rfq.AddItem("3", "3", null, null, "catering", 1m, "unit", false, false);

        rfq.RemoveItem(first.Id);

        rfq.Items.Should().HaveCount(2);
        rfq.Items.Select(i => i.LineNo).Should().BeEquivalentTo([1, 2]);
        rfq.Items.Should().Contain(i => i.Id == third.Id && i.LineNo == 2);
    }

    /// <summary>Drives the aggregate through every real transition up to (not including) the
    /// requested state, using only the domain's own methods - the same "walk the machine" pattern
    /// used to prove the theory-table Cancel test above without duplicating setup per case.</summary>
    private static void AdvanceTo(Rfq rfq, RfqState state)
    {
        if (state == RfqState.Draft) return;
        rfq.SubmitForReview();
        if (state == RfqState.InternalReview) return;
        rfq.Approve(ApproverId);
        if (state == RfqState.Approved) return;
        rfq.Publish();
        if (state == RfqState.Published) return;
        rfq.OpenSubmissionWindow();
        if (state == RfqState.SubmissionOpen) return;
        rfq.CloseSubmissionWindow(null, isEarlyClose: false);
    }
}
