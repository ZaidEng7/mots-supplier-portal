using FluentAssertions;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>FEAT-09.1..09.6. State list/transitions verified directly against
/// BUSINESS-PROCESSES.md §4.1 - see Proposal.cs's own doc comments for the exact quoted transition
/// rows this exercises. Cross-aggregate facts (submissionCloseAt, requiredRfqItemIds,
/// mandatoryRequirementIds, rfqSubmissionOpen) are passed as plain parameters here, matching how
/// PublishRfqHandler/InviteSupplierHandler already resolve cross-aggregate guards outside the
/// domain method itself.</summary>
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

    /// <summary>
    /// §7.2's PRICE_NON_POSITIVE, asserted at the aggregate rather than only through the endpoint.
    /// The API validator says the same thing; an invariant that lives only in a validator is one
    /// route away from not existing.
    /// </summary>
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
        // The control: the guard rejects zero and below, not every small number.
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
    // T-066: completeness refusals throw their own exception type now, so the API can answer
    // §12.5's 422 with a code naming what is missing rather than the 400 they shared with the
    // window and wrong-state refusals - which still throw DomainException.
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

    /// <summary>Revert-to-red proof, same discipline as EPIC-07's window automation: this is the
    /// exact guard that makes late submission impossible even with a stale client clock -
    /// submissionCloseAt is a server-resolved fact (from the loaded Rfq), never client-supplied, and
    /// DateTimeOffset.UtcNow inside Submit is the server's own clock, not anything the request body
    /// could influence.</summary>
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
}
