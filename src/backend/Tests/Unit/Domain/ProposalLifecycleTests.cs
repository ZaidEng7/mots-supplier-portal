// The middle of the bid state machine, which nothing could reach.
//
// Six of eleven states were never assigned anywhere in production code, so a bid went from draft to submitted to
// an outcome and skipped evaluation intake, the clarification loop and shortlisting entirely.
//
// The third instance of this class of defect in this codebase, after three tender states and an entire
// unreachable feature.
//
// Driven through the aggregate's real transitions rather than a test seam, which is the convention the other
// domain state-machine tests follow. An empty required-item set is a bid with nothing mandatory outstanding,
// which is what the submit guard checks.
//
//
// THE CLARIFICATION LOOP IS ASSERTED AS A LOOP
//
// The written table marks it as repeatable, so asserting one pass would not prove what the table describes. A
// second pass is asserted too.
//
// Its own guard requires a reason, and the control shows that with a reason it succeeds, so the guard can be
// satisfied as well as refuse.
//
//
// THE REGRESSION THAT MAKING THE MIDDLE REACHABLE CAUSED
//
// Moving the winner out of submitted broke the award, which accepted only submitted bids, and produced an
// uncaught refusal and a server error on executing an award. One test here is the guard for that, with a control
// showing a draft is still refused so the widening did not become "anything goes".
//
// A final assertion covers the predicate six queries now depend on. If a state is listed there but unreachable,
// those queries filter for something that never exists, which is the defect this work closed, reintroduced
// through the back door.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class ProposalLifecycleTests
{
    private static Proposal SubmittedProposal(string code = "PRP-2026-000001")
    {
        var proposal = Proposal.Create(code, Guid.CreateVersion7(), Guid.CreateVersion7());
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
        var proposal = Proposal.Create("PRP-2026-000003", Guid.CreateVersion7(), Guid.CreateVersion7());

        var act = () => proposal.OpenForReview();

        act.Should().Throw<DomainException>().WithMessage("*only 'Submitted' is valid*");
    }

    [Fact]
    public void The_clarification_loop_runs_and_can_repeat()
    {
        var proposal = UnderReviewProposal();

        proposal.RequestClarification("Please confirm the delivery window.");
        proposal.State.Should().Be(ProposalState.ClarificationRequested);
        proposal.ClarificationReason.Should().Be("Please confirm the delivery window.");

        proposal.RecordRevision();
        proposal.State.Should().Be(ProposalState.Revised);
        proposal.RevisionNumber.Should().Be(2, "the original submission is revision 1");

        proposal.ReturnToReview();
        proposal.State.Should().Be(ProposalState.UnderReview);

        proposal.RequestClarification("And the warranty term.");
        proposal.RecordRevision();
        proposal.RevisionNumber.Should().Be(3);
    }

    [Fact]
    public void A_clarification_without_a_reason_is_refused()
    {
        var proposal = UnderReviewProposal();

        var act = () => proposal.RequestClarification("   ");

        act.Should().Throw<DomainException>().WithMessage("*reason is required*");

        proposal.RequestClarification("A real question.");
        proposal.State.Should().Be(ProposalState.ClarificationRequested);
    }

    [Fact]
    public void Shortlisting_requires_UnderReview()
    {
        var proposal = UnderReviewProposal();
        proposal.Shortlist();
        proposal.State.Should().Be(ProposalState.Shortlisted);

        var act = () => proposal.Shortlist();
        act.Should().Throw<DomainException>().WithMessage("*only 'UnderReview' is valid*");
    }

    [Fact]
    public void The_award_path_still_accepts_every_state_that_can_now_reach_it()
    {
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

        var draft = Proposal.Create("PRP-2026-000099", Guid.CreateVersion7(), Guid.CreateVersion7());
        var act = () => draft.Award();
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Every_state_in_the_evaluation_set_is_one_the_machine_can_actually_reach()
    {
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
