using FluentAssertions;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>
/// T-051: the middle of BUSINESS-PROCESSES.md §4.1's proposal machine, which nothing could reach.
///
/// <para>Six of eleven states were never assigned anywhere in production code, so a proposal went
/// Draft → Submitted → outcome and skipped evaluation intake, the clarification loop and
/// shortlisting entirely. Third instance of this class after T3-36's three RFQ states and EPIC-17's
/// unreachable epic.</para>
/// </summary>
public sealed class ProposalLifecycleTests
{
    /// <summary>
    /// Driven through the aggregate's real transitions rather than a test seam - the convention the
    /// other domain state-machine tests here already follow. An empty required-item set is a
    /// proposal with nothing mandatory outstanding, which is what Submit's own guard checks.
    /// </summary>
    private static Proposal SubmittedProposal(string code = "PRP-2026-000001")
    {
        var proposal = Proposal.Create(code, Guid.CreateVersion7(), Guid.CreateVersion7());
        // Submit's own guards: terms carry the validity window it checks for.
        proposal.SetCommercialTerms(
            "SYP", "Net 30", "FOB", null, null, null,
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
        proposal.Submit(
            rfqSubmissionOpen: true,
            submissionCloseAt: DateTimeOffset.UtcNow.AddDays(1),
            requiredRfqItemIds: new HashSet<Guid>(),
            mandatoryRequirementIds: new HashSet<Guid>());
        return proposal;
    }

    private static Proposal UnderReviewProposal(string code = "PRP-2026-000001")
    {
        var proposal = SubmittedProposal(code);
        proposal.OpenForReview();
        return proposal;
    }

    [Fact]
    public void Intake_moves_a_submitted_proposal_to_UnderReview()
    {
        var proposal = SubmittedProposal("PRP-2026-000002");

        proposal.OpenForReview();

        proposal.State.Should().Be(ProposalState.UnderReview);
    }

    [Fact]
    public void Intake_refuses_a_draft()
    {
        // The control: a guard that accepts everything would pass the test above.
        var proposal = Proposal.Create("PRP-2026-000003", Guid.CreateVersion7(), Guid.CreateVersion7());

        var act = () => proposal.OpenForReview();

        act.Should().Throw<DomainException>().WithMessage("*only 'Submitted' is valid*");
    }

    [Fact]
    public void The_clarification_loop_runs_and_can_repeat()
    {
        // §4.1 marks the loop "(ClarificationRequested → Revised → UnderReview)*" - repeatable, so
        // asserting one pass would not prove what the table describes.
        var proposal = UnderReviewProposal();

        proposal.RequestClarification("Please confirm the delivery window.");
        proposal.State.Should().Be(ProposalState.ClarificationRequested);
        proposal.ClarificationReason.Should().Be("Please confirm the delivery window.");

        proposal.RecordRevision();
        proposal.State.Should().Be(ProposalState.Revised);
        proposal.RevisionNumber.Should().Be(2, "the original submission is revision 1");

        proposal.ReturnToReview();
        proposal.State.Should().Be(ProposalState.UnderReview);

        // Second pass - the asterisk in the table.
        proposal.RequestClarification("And the warranty term.");
        proposal.RecordRevision();
        proposal.RevisionNumber.Should().Be(3);
    }

    [Fact]
    public void A_clarification_without_a_reason_is_refused()
    {
        // §4.1's own guard: "Reason; specific questions".
        var proposal = UnderReviewProposal();

        var act = () => proposal.RequestClarification("   ");

        act.Should().Throw<DomainException>().WithMessage("*reason is required*");

        // Control: with a reason it succeeds, so the guard can be satisfied as well as refuse.
        proposal.RequestClarification("A real question.");
        proposal.State.Should().Be(ProposalState.ClarificationRequested);
    }

    [Fact]
    public void Shortlisting_requires_UnderReview()
    {
        var proposal = UnderReviewProposal();
        proposal.Shortlist();
        proposal.State.Should().Be(ProposalState.Shortlisted);

        // Control on the other side: a second shortlist is refused, so the guard is real.
        var act = () => proposal.Shortlist();
        act.Should().Throw<DomainException>().WithMessage("*only 'UnderReview' is valid*");
    }

    [Fact]
    public void The_award_path_still_accepts_every_state_that_can_now_reach_it()
    {
        // Making the middle reachable moved the winner out of Submitted, and Award() accepted only
        // Submitted - which produced an uncaught DomainException and a 500 on award/execute. This is
        // the regression guard for that.
        var fromSubmitted = SubmittedProposal("PRP-2026-000010");
        fromSubmitted.Award();
        fromSubmitted.State.Should().Be(ProposalState.Awarded, "the pre-evaluation award path still works");

        var fromUnderReview = UnderReviewProposal("PRP-2026-000011");
        fromUnderReview.Award();
        fromUnderReview.State.Should().Be(ProposalState.Awarded, "this is the state intake now leaves the winner in");

        var fromShortlisted = UnderReviewProposal("PRP-2026-000012");
        fromShortlisted.Shortlist();
        fromShortlisted.Award();
        fromShortlisted.State.Should().Be(ProposalState.Awarded, "§4.1's canonical path");

        // Control: a Draft is still refused, so the widening did not become "anything goes".
        var draft = Proposal.Create("PRP-2026-000099", Guid.CreateVersion7(), Guid.CreateVersion7());
        var act = () => draft.Award();
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Every_state_in_the_evaluation_set_is_one_the_machine_can_actually_reach()
    {
        // The predicate six queries now depend on. If a state is listed here but unreachable, those
        // queries filter for something that never exists - which is the defect this batch closed,
        // reintroduced through the back door.
        ProposalStates.InEvaluation.Should().BeEquivalentTo(new[]
        {
            ProposalState.Submitted,
            ProposalState.UnderReview,
            ProposalState.ClarificationRequested,
            ProposalState.Revised,
            ProposalState.Shortlisted,
        });
    }
}
