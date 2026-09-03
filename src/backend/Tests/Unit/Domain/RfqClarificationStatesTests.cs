using FluentAssertions;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>
/// T3-36: the three states BUSINESS-PROCESSES.md §3.1 defines and no code path could reach.
///
/// <para>Asserted at the aggregate because that is where the guards live. The endpoints get their own
/// tests for status codes and permissions; these are about the machine.</para>
/// </summary>
public sealed class RfqClarificationStatesTests
{
    private static Rfq UnderEvaluation()
    {
        var rfq = Rfq.Create("RFQ-2026-000001", Guid.NewGuid(), "طلب", "RFQ", null, null, "SYP",
            null, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(8), null, null);

        rfq.AddItem("بند", "Item", null, null, "catering", 5, "unit", true, false);
        rfq.BindEvaluationTemplate(Guid.NewGuid(), 1, "{}");
        // §3.1's own guard on Submit for review: "≥1 candidate supplier identified".
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
        // The negatives, each with the positive above as its control.
        var rfq = UnderEvaluation();

        var recommendFirst = () => rfq.RecordRecommendation();
        recommendFirst.Should().Throw<DomainException>().WithMessage("*only 'Shortlisting' is valid*");

        var resolveWithoutRequesting = () => rfq.ResolveClarification();
        resolveWithoutRequesting.Should().Throw<DomainException>().WithMessage("*only 'Clarification' is valid*");

        rfq.RequestClarification("Ask");
        var shortlistFromClarification = () => rfq.BeginShortlisting();
        shortlistFromClarification.Should().Throw<DomainException>().WithMessage("*only 'UnderEvaluation' is valid*");
    }

    /// <summary>
    /// The back-compatibility assertion the batch asks for: no backfill, so rows written before
    /// T3-36 sit in UnderEvaluation and must still route straight to AwardApproval. A guard that
    /// only admitted the new path would strand every RFQ that exists today.
    /// </summary>
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
        // §3.1: "any pre-Awarded | Cancelled". Asserted through the aggregate, not only the map.
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
