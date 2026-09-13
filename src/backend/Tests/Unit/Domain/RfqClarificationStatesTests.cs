// The three tender states the written process defines and no code path could reach.
//
// Asserted at the aggregate, because that is where the guards live. The endpoints get their own tests for status
// codes and permissions; these are about the machine.
//
// Each negative has the positive above it as its control.
//
// One guard comes from the written process directly: sending a tender for review requires at least one candidate
// supplier identified.
//
//
// THE BACK-COMPATIBILITY ASSERTION
//
// There is no backfill, so tenders written before these states existed sit in the older state and must still
// route straight to award approval.
//
// A guard that only admitted the new path would strand every tender that exists today.
//
// Cancellation from any state before an award is asserted through the aggregate rather than only through the map
// of legal moves.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class RfqClarificationStatesTests
{
    private static Rfq UnderEvaluation()
    {
        var rfq = Rfq.Create("RFQ-2026-000001", Guid.NewGuid(), "طلب", "RFQ", null, null, "SYP",
            null, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(8), null, null);

        rfq.AddItem("بند", "Item", null, null, "catering", 5, "unit", true, false);
        rfq.BindEvaluationTemplate(Guid.NewGuid(), 1, "{}");
        rfq.InviteSupplier(Guid.NewGuid());
        rfq.SubmitForReview();
        rfq.Approve(Guid.NewGuid());
        rfq.Publish();
        rfq.OpenSubmissionWindow();
        rfq.CloseSubmissionWindow(reason: null, isEarlyClose: false);
        rfq.OpenEvaluation();

        return rfq;
    }

    [Fact]
    public void Requesting_clarification_moves_UnderEvaluation_to_Clarification_and_back_again()
    {
        var rfq = UnderEvaluation();

        rfq.RequestClarification("Missing delivery schedule");
        rfq.State.Should().Be(RfqState.Clarification);

        rfq.ResolveClarification();
        rfq.State.Should().Be(RfqState.UnderEvaluation, "§3.1: Clarification returns to UnderEvaluation");
    }

    [Fact]
    public void A_clarification_request_needs_the_reason_the_table_names_as_its_guard()
    {
        var rfq = UnderEvaluation();

        var act = () => rfq.RequestClarification("   ");

        act.Should().Throw<DomainException>().WithMessage("*reason is required*");
        rfq.State.Should().Be(RfqState.UnderEvaluation, "a refused transition must not move the aggregate");
    }

    [Fact]
    public void Shortlisting_and_Recommendation_run_in_the_order_the_table_gives()
    {
        var rfq = UnderEvaluation();

        rfq.BeginShortlisting();
        rfq.State.Should().Be(RfqState.Shortlisting);

        rfq.RecordRecommendation();
        rfq.State.Should().Be(RfqState.Recommendation);

        rfq.EnterAwardApproval();
        rfq.State.Should().Be(RfqState.AwardApproval);
    }

    [Fact]
    public void The_new_states_cannot_be_entered_out_of_order()
    {
        var rfq = UnderEvaluation();

        var recommendFirst = () => rfq.RecordRecommendation();
        recommendFirst.Should().Throw<DomainException>().WithMessage("*only 'Shortlisting' is valid*");

        var resolveWithoutRequesting = () => rfq.ResolveClarification();
        resolveWithoutRequesting.Should().Throw<DomainException>().WithMessage("*only 'Clarification' is valid*");

        rfq.RequestClarification("Ask");
        var shortlistFromClarification = () => rfq.BeginShortlisting();
        shortlistFromClarification.Should().Throw<DomainException>().WithMessage("*only 'UnderEvaluation' is valid*");
    }

    [Fact]
    public void An_RFQ_in_the_old_state_still_transitions_as_it_did_before()
    {
        var rfq = UnderEvaluation();

        rfq.EnterAwardApproval();

        rfq.State.Should().Be(RfqState.AwardApproval);
    }

    [Fact]
    public void The_new_states_are_all_cancellable()
    {
        foreach (var enter in new Action<Rfq>[]
        {
            r => r.RequestClarification("Ask"),
            r => r.BeginShortlisting(),
            r => { r.BeginShortlisting(); r.RecordRecommendation(); },
        })
        {
            var rfq = UnderEvaluation();
            enter(rfq);

            rfq.Cancel("No longer required");

            rfq.State.Should().Be(RfqState.Cancelled);
        }
    }
}
