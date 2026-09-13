// The bid aggregate: its states, its guards, and the two ways a bid can end without being decided.
//
// The transitions are checked against the written process directly; the aggregate's own header carries the
// quoted rows.
//
// Cross-aggregate facts, the closing time, the required line identifiers, the mandatory requirement identifiers
// and whether the window is open, are passed in as plain parameters, matching how the handlers already resolve
// cross-aggregate guards outside the domain method.
//
//
// A PRICE GUARD ASSERTED AT THE AGGREGATE, NOT ONLY THROUGH THE ENDPOINT
//
// The request validator says the same thing, and an invariant that lives only in a validator is one route away
// from not existing.
//
// Its control shows the guard rejects zero and below rather than every small number.
//
//
// COMPLETENESS REFUSALS HAVE THEIR OWN EXCEPTION TYPE
//
// So the endpoint can answer with a code naming what is missing, rather than the generic answer they used to
// share with the window and wrong-state refusals, which still throw the general kind.
//
//
// THE LATE-SUBMISSION GUARD, WITH A REVERT-TO-RED
//
// Same discipline as the tender window automation. This is the exact guard that makes a late submission
// impossible even with a stale client clock: the closing time is a server-resolved fact read off the loaded
// tender and never supplied by the caller, and the comparison uses the server's own clock, which nothing in the
// request body can influence.
//
//
// TWO WAYS A BID ENDS WITHOUT A DECISION, AND WHY THEY ARE TWO
//
// A draft that survived the submission window used to stay a draft forever: the supplier's dashboard kept
// counting a bid that could never be submitted, and nothing in the record said what had happened to it.
//
// A tender's cancellation voiding its open bids carries NO assumption tag in the written rules, so its previous
// half-enforcement, notify everyone and move nothing, was a confirmed rule going unenforced.
//
// They are two states rather than one because "you ran out of time" and "the tender was withdrawn" are
// different messages, and a supplier reading their list has to be able to tell them apart.
//
// Each has a guard the other way: a job that runs every five minutes must not be able to re-terminate a decided
// outcome, and a bid that already reached a terminal state is not rewritten, because a withdrawn bid was
// withdrawn and the supplier was told so. A submitted bid cannot lapse at all; it can only be cancelled with
// its tender.
//
// And a control: a bid that made the deadline missed nothing.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Suppliers;

public class ProposalTests
{
    private static readonly Guid RfqId = Guid.CreateVersion7();
    private static readonly Guid SupplierId = Guid.CreateVersion7();
    private static readonly Guid RequiredItemId = Guid.CreateVersion7();
    private static readonly Guid MandatoryRequirementId = Guid.CreateVersion7();

    private static Proposal CreateDraft() => Proposal.Create("PRP-2026-000001", RfqId, SupplierId);

    private static Proposal CreateReadyToSubmit(DateOnly? validityEnd = null)
    {
        var proposal = CreateDraft();
        proposal.SetItemPricing(RequiredItemId, 10m, 5m, discount: null, leadTimeDays: 3, notesAr: null, notesEn: null);
        proposal.AnswerRequirement(MandatoryRequirementId, "نعم", "Yes");
        proposal.SetCommercialTerms("SYP", "Net 30", "FOB", "3 days", "3 days", null,
            DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), validityEnd ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)));
        return proposal;
    }

    private static readonly IReadOnlySet<Guid> RequiredItems = new HashSet<Guid> { RequiredItemId };
    private static readonly IReadOnlySet<Guid> MandatoryRequirements = new HashSet<Guid> { MandatoryRequirementId };

    [Fact]
    public void New_proposal_starts_in_draft()
    {
        CreateDraft().State.Should().Be(ProposalState.Draft);
    }

    [Fact]
    public void SetItemPricing_upserts_by_rfq_item_id_rather_than_duplicating()
    {
        var proposal = CreateDraft();

        proposal.SetItemPricing(RequiredItemId, 10m, 5m, null, null, null, null);
        proposal.SetItemPricing(RequiredItemId, 10m, 7.5m, null, null, null, null);

        proposal.Items.Should().ContainSingle();
        proposal.Items.Single().UnitPrice.Should().Be(7.5m);
    }

    [Fact]
    public void LineTotal_is_computed_from_quantity_price_and_discount_never_stored()
    {
        var proposal = CreateDraft();

        proposal.SetItemPricing(RequiredItemId, 10m, 5m, discount: 2m, leadTimeDays: null, notesAr: null, notesEn: null);

        proposal.Items.Single().LineTotal.Should().Be(48m); // 10*5 - 2
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void SetItemPricing_rejects_a_non_positive_unit_price(decimal unitPrice)
    {
        var proposal = CreateDraft();

        var act = () => proposal.SetItemPricing(RequiredItemId, 1m, unitPrice, null, null, null, null);

        act.Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public void SetItemPricing_accepts_the_smallest_positive_price()
    {
        var proposal = CreateDraft();

        proposal.SetItemPricing(RequiredItemId, 1m, 0.01m, null, null, null, null);

        proposal.Items.Should().ContainSingle(i => i.UnitPrice == 0.01m);
    }

    [Fact]
    public void SetItemPricing_rejects_non_positive_quantity()
    {
        var proposal = CreateDraft();

        var act = () => proposal.SetItemPricing(RequiredItemId, 0m, 5m, null, null, null, null);

        act.Should().Throw<DomainException>().WithMessage("*Quantity must be positive*");
    }

    [Fact]
    public void Edits_are_rejected_once_the_proposal_leaves_draft()
    {
        var proposal = CreateReadyToSubmit();
        proposal.Submit(true, DateTimeOffset.UtcNow.AddHours(1), RequiredItems, MandatoryRequirements);

        var act = () => proposal.SetItemPricing(RequiredItemId, 1m, 1m, null, null, null, null);

        act.Should().Throw<DomainException>().WithMessage("*only 'Draft' allows edits*");
    }

    [Fact]
    public void AnswerRequirement_upserts_by_requirement_id()
    {
        var proposal = CreateDraft();

        proposal.AnswerRequirement(MandatoryRequirementId, "أولاً", "First");
        proposal.AnswerRequirement(MandatoryRequirementId, "ثانياً", "Second");

        proposal.RequirementAnswers.Should().ContainSingle();
        proposal.RequirementAnswers.Single().AnswerEn.Should().Be("Second");
    }

    [Fact]
    public void Submit_requires_all_required_items_priced()
    {
        var proposal = CreateDraft();
        proposal.AnswerRequirement(MandatoryRequirementId, "نعم", "Yes");
        proposal.SetCommercialTerms("SYP", null, null, null, null, null, null, DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)));

        var act = () => proposal.Submit(true, DateTimeOffset.UtcNow.AddHours(1), RequiredItems, MandatoryRequirements);

        act.Should().Throw<ProposalIncompleteException>().WithMessage("*required RFQ items must be priced*");
    }

    [Fact]
    public void Submit_requires_all_mandatory_requirements_answered()
    {
        var proposal = CreateDraft();
        proposal.SetItemPricing(RequiredItemId, 10m, 5m, null, null, null, null);
        proposal.SetCommercialTerms("SYP", null, null, null, null, null, null, DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)));

        var act = () => proposal.Submit(true, DateTimeOffset.UtcNow.AddHours(1), RequiredItems, MandatoryRequirements);

        act.Should().Throw<ProposalIncompleteException>().WithMessage("*mandatory requirements must be answered*");
    }

    [Fact]
    public void Submit_requires_a_validity_end_date()
    {
        var proposal = CreateDraft();
        proposal.SetItemPricing(RequiredItemId, 10m, 5m, null, null, null, null);
        proposal.AnswerRequirement(MandatoryRequirementId, "نعم", "Yes");

        var act = () => proposal.Submit(true, DateTimeOffset.UtcNow.AddHours(1), RequiredItems, MandatoryRequirements);

        act.Should().Throw<ProposalIncompleteException>().WithMessage("*validity end date is required*");
    }

    [Fact]
    public void Submit_succeeds_when_everything_required_is_present()
    {
        var proposal = CreateReadyToSubmit();

        proposal.Submit(true, DateTimeOffset.UtcNow.AddHours(1), RequiredItems, MandatoryRequirements);

        proposal.State.Should().Be(ProposalState.Submitted);
        proposal.SubmittedAt.Should().NotBeNull();
    }

    [Fact]
    public void Submit_is_refused_once_the_submission_window_has_closed_even_with_a_stale_client_clock()
    {
        var proposal = CreateReadyToSubmit();
        var submissionCloseAt = DateTimeOffset.UtcNow.AddMilliseconds(-1); // already closed, server-side

        var act = () => proposal.Submit(true, submissionCloseAt, RequiredItems, MandatoryRequirements);

        act.Should().Throw<DomainException>().WithMessage("*submission window has closed*");
        proposal.State.Should().Be(ProposalState.Draft, "a refused submission must not silently half-transition");
    }

    [Fact]
    public void Submit_is_refused_when_the_rfq_is_not_in_submission_open()
    {
        var proposal = CreateReadyToSubmit();

        var act = () => proposal.Submit(false, DateTimeOffset.UtcNow.AddHours(1), RequiredItems, MandatoryRequirements);

        act.Should().Throw<DomainException>().WithMessage("*not currently accepting submissions*");
    }

    [Fact]
    public void Withdraw_is_allowed_from_draft_while_the_window_is_open()
    {
        var proposal = CreateDraft();

        proposal.Withdraw("Changed our mind", rfqSubmissionOpen: true);

        proposal.State.Should().Be(ProposalState.Withdrawn);
        proposal.WithdrawReason.Should().Be("Changed our mind");
    }

    [Fact]
    public void Withdraw_is_allowed_from_submitted_while_the_window_is_open()
    {
        var proposal = CreateReadyToSubmit();
        proposal.Submit(true, DateTimeOffset.UtcNow.AddHours(1), RequiredItems, MandatoryRequirements);

        proposal.Withdraw("Pricing error", rfqSubmissionOpen: true);

        proposal.State.Should().Be(ProposalState.Withdrawn);
    }

    [Fact]
    public void Withdraw_is_refused_once_the_submission_window_has_closed()
    {
        var proposal = CreateReadyToSubmit();
        proposal.Submit(true, DateTimeOffset.UtcNow.AddHours(1), RequiredItems, MandatoryRequirements);

        var act = () => proposal.Withdraw("Too late", rfqSubmissionOpen: false);

        act.Should().Throw<DomainException>().WithMessage("*submission window is no longer open*");
    }

    [Fact]
    public void Withdraw_requires_a_reason()
    {
        var proposal = CreateDraft();

        var act = () => proposal.Withdraw("", rfqSubmissionOpen: true);

        act.Should().Throw<DomainException>().WithMessage("*reason is required*");
    }

    [Fact]
    public void Lapse_only_applies_to_a_draft_and_is_terminal()
    {
        var draft = CreateDraft();

        draft.Lapse();

        draft.State.Should().Be(ProposalState.Lapsed);
        Proposal.AllowedNextFrom(ProposalState.Lapsed).Should().BeEmpty("Lapsed is terminal");

        ((Action)(() => draft.Lapse())).Should().Throw<DomainException>().WithMessage("*only 'Draft' lapses*");
    }

    [Fact]
    public void A_submitted_proposal_does_not_lapse()
    {
        var proposal = CreateReadyToSubmit();
        proposal.Submit(true, DateTimeOffset.UtcNow.AddHours(1), RequiredItems, MandatoryRequirements);

        ((Action)(() => proposal.Lapse())).Should().Throw<DomainException>();
        proposal.State.Should().Be(ProposalState.Submitted);
    }

    [Fact]
    public void CancelWithRfq_closes_a_live_proposal_and_leaves_a_resolved_one_alone()
    {
        var live = CreateReadyToSubmit();
        live.Submit(true, DateTimeOffset.UtcNow.AddHours(1), RequiredItems, MandatoryRequirements);

        live.CancelWithRfq();

        live.State.Should().Be(ProposalState.Cancelled);
        Proposal.AllowedNextFrom(ProposalState.Cancelled).Should().BeEmpty("Cancelled is terminal");

        var withdrawn = CreateDraft();
        withdrawn.Withdraw("Changed our mind.", rfqSubmissionOpen: true);
        ((Action)(() => withdrawn.CancelWithRfq())).Should().Throw<DomainException>().WithMessage("*already resolved*");
        withdrawn.State.Should().Be(ProposalState.Withdrawn);
    }

    [Fact]
    public void The_two_new_states_are_distinguishable_and_not_interchangeable()
    {
        ProposalState.Lapsed.Should().NotBe(ProposalState.Cancelled);
        Proposal.AllowedNextFrom(ProposalState.Draft).Should().Contain(ProposalState.Lapsed);
        Proposal.AllowedNextFrom(ProposalState.Draft).Should().Contain(ProposalState.Cancelled);
        Proposal.AllowedNextFrom(ProposalState.Submitted).Should().NotContain(ProposalState.Lapsed);
        Proposal.AllowedNextFrom(ProposalState.Submitted).Should().Contain(ProposalState.Cancelled);
    }
}
