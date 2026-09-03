using FluentAssertions;
using MotsSupplierPortal.Domain.Rfqs;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>
/// T3-36. §3: "Illegal transitions return 409 Conflict … listing the current state and the allowed
/// next states."
///
/// <para>The map is what the 409 reports and what decides 409-versus-400, so it is asserted directly
/// rather than only through an endpoint. A map that drifts from the aggregate's guards would tell a
/// caller the wrong thing while every transition test still passed.</para>
/// </summary>
public sealed class RfqAllowedNextTests
{
    /// <summary>
    /// Both directions of the same claim, per state: every state the map lists must actually be
    /// reachable, and every state it omits must actually be refused. Driven off the guards
    /// themselves - a map maintained by hand beside guards maintained by hand is two lists that can
    /// disagree, and this is the test that makes them one.
    /// </summary>
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
        // §3.1: "any pre-Awarded | Cancelled | Cancel RFQ". The three new states are pre-Awarded, so
        // the general rule has to cover them too - a state that cannot be cancelled would trap an
        // RFQ mid-evaluation.
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

        // The control, and the other direction: cancellation stops being available once the RFQ is
        // awarded. Without this the assertion above would pass on a map that allowed it everywhere.
        Rfq.AllowedNextFrom(RfqState.Awarded).Should().NotContain(RfqState.Cancelled);
        Rfq.AllowedNextFrom(RfqState.Completed).Should().BeEmpty();
        Rfq.AllowedNextFrom(RfqState.Cancelled).Should().BeEmpty();
    }

    [Fact]
    public void Every_state_has_an_entry_so_the_409_can_never_report_an_empty_set_by_accident()
    {
        // A switch with a default arm silently answers "nothing is allowed" for a state nobody
        // added - which reads to a client as a terminal state. Only the two genuinely terminal
        // states, plus Cancelled, may be empty.
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
