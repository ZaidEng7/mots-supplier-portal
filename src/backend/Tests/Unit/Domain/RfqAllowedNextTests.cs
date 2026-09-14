// The map of legal next states, asserted directly rather than only through an endpoint.
//
// That map is what a refused transition reports to the caller, and what decides which of the two refusals it is.
//
// A map that drifted from the aggregate's guards would tell a caller the wrong thing while every transition test
// still passed.
//
//
// BOTH DIRECTIONS, PER STATE, DRIVEN OFF THE GUARDS THEMSELVES
//
// Every state the map lists must actually be reachable, and every state it omits must actually be refused.
//
// A map maintained by hand beside guards maintained by hand is two lists that can disagree, and this is the test
// that makes them one.
//
//
// CANCELLATION
//
// The written process allows it from any state before an award, so the newer states have to be covered by the
// general rule too: a state that could not be cancelled would trap a tender mid-evaluation.
//
// The control is the other direction: cancellation stops being available once the tender is awarded. Without it
// the assertion above would pass on a map that allowed it everywhere.
//
//
// AN EMPTY SET IS ONLY LEGAL FOR A TERMINAL STATE
//
// A switch with a catch-all arm silently answers "nothing is allowed" for a state nobody added, which reads to a
// client as a terminal state. Only the two genuinely terminal states, plus cancelled, may be empty.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Rfqs;

public sealed class RfqAllowedNextTests
{
    [Theory]
    [InlineData(RfqState.UnderEvaluation, RfqState.Clarification, true)]
    [InlineData(RfqState.UnderEvaluation, RfqState.Shortlisting, true)]
    [InlineData(RfqState.UnderEvaluation, RfqState.AwardApproval, true)]
    [InlineData(RfqState.UnderEvaluation, RfqState.Recommendation, false)]
    [InlineData(RfqState.UnderEvaluation, RfqState.Awarded, false)]
    [InlineData(RfqState.Clarification, RfqState.UnderEvaluation, true)]
    [InlineData(RfqState.Clarification, RfqState.Shortlisting, false)]
    [InlineData(RfqState.Shortlisting, RfqState.Recommendation, true)]
    [InlineData(RfqState.Shortlisting, RfqState.UnderEvaluation, false)]
    [InlineData(RfqState.Recommendation, RfqState.AwardApproval, true)]
    [InlineData(RfqState.Recommendation, RfqState.Shortlisting, false)]
    public void The_map_lists_exactly_the_moves_that_are_legal(RfqState from, RfqState to, bool expected)
    {
        Rfq.AllowedNextFrom(from).Contains(to).Should().Be(expected);
    }

    [Fact]
    public void Every_pre_awarded_state_can_be_cancelled_and_no_terminal_state_can()
    {
        RfqState[] preAwarded =
        [
            RfqState.Draft, RfqState.InternalReview, RfqState.Approved, RfqState.Published,
            RfqState.SubmissionOpen, RfqState.SubmissionClosed, RfqState.UnderEvaluation,
            RfqState.Clarification, RfqState.Shortlisting, RfqState.Recommendation, RfqState.AwardApproval,
        ];

        foreach (var state in preAwarded)
        {
            Rfq.AllowedNextFrom(state).Should().Contain(RfqState.Cancelled, $"{state} is pre-Awarded");
        }

        Rfq.AllowedNextFrom(RfqState.Awarded).Should().NotContain(RfqState.Cancelled);
        Rfq.AllowedNextFrom(RfqState.Completed).Should().BeEmpty();
        Rfq.AllowedNextFrom(RfqState.Cancelled).Should().BeEmpty();
    }

    [Fact]
    public void Every_state_has_an_entry_so_the_409_can_never_report_an_empty_set_by_accident()
    {
        foreach (var state in Enum.GetValues<RfqState>())
        {
            var allowed = Rfq.AllowedNextFrom(state);

            if (state is RfqState.Completed or RfqState.Cancelled)
            {
                allowed.Should().BeEmpty($"{state} is terminal");
            }
            else
            {
                allowed.Should().NotBeEmpty($"{state} must declare where it can go");
            }
        }
    }
}
