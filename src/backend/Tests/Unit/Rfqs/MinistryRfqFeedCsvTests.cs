// The ministry's RFQ feed: seven columns, and the mapping from thirteen proposal states to their three words.
//
// EVERY PROPOSAL STATE IS ASSERTED, not a sample, and the absence of a state is asserted too. The mapping is a
// switch whose default arm is "Received", so a state nobody thought about does not fall through to something
// harmless - it reports a quote the ministry never received. That is the wrong direction to be wrong in: an
// invented quote inflates a supplier's response rate, and nothing downstream could tell.
//
// NO PROPOSAL IS Pending, NOT No Quote, and it is worth being precise about why. "No Quote" is an answer: the
// supplier said no, pulled out, or let the window close. An invitation nobody has responded to yet is an open
// question. Their dashboard measures response rates, so collapsing the two would report every unanswered
// invitation as a refusal.
//
// FOUR ENDINGS SHARE ONE WORD - Declined, Withdrawn, Lapsed and Cancelled all become No Quote - and the test
// names all four rather than testing one and assuming. They are genuinely different events and their
// vocabulary has no room for the difference, so the loss is deliberate and recorded rather than accidental.
//
// THE COLUMN COUNT is asserted for the same reason as the supplier feed's: a row out of step with the header
// shifts every column after it and the file still loads.
//
// QUOTATIONCURRENCY TRAVELS WITH THE TOTAL, and the test asserts it is populated whenever a total is. A column
// of bare amounts in two currencies is one a dashboard sums into a figure that means nothing.

namespace MotsSupplierPortal.Tests.Unit.Rfqs;

using FluentAssertions;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Proposals;
using Xunit;

public sealed class MinistryRfqFeedCsvTests
{
    private static MinistryRfqFeedRecord Record(
        ProposalState? state = null, decimal? total = null, string? currency = null, string? proposalCode = null,
        string rfqCurrency = "SYP") =>
        new("RFQ-2026-000123", "SUP-2026-000042", new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero),
            proposalCode, state, currency, rfqCurrency, total);

    private static int At(string column) => MinistryRfqFeedCsv.Columns.ToList().IndexOf(column);

    [Fact]
    public void A_row_has_exactly_as_many_cells_as_the_header_has_columns()
    {
        MinistryRfqFeedCsv.Cells(Record()).Should().HaveCount(MinistryRfqFeedCsv.Columns.Count);
        MinistryRfqFeedCsv.Cells(Record(ProposalState.Submitted, 7900m, "SYP", "PRP-2026-000004"))
            .Should().HaveCount(MinistryRfqFeedCsv.Columns.Count);
    }

    [Fact]
    public void An_invitation_nobody_has_answered_is_pending_rather_than_a_refusal()
    {
        var cells = MinistryRfqFeedCsv.Cells(Record());

        cells[At("QuoteStatus")].Should().Be(
            "Pending",
            "No Quote is an answer - said no, pulled out, ran out of time - and an unanswered invitation is an "
            + "open question; collapsing the two reports every silence as a refusal");
        cells[At("QuotationNo")].Should().BeNull();
        cells[At("QuotationTotal")].Should().BeNull();
    }

    [Theory]
    [InlineData(ProposalState.Draft, "Pending")]
    [InlineData(ProposalState.Submitted, "Received")]
    [InlineData(ProposalState.UnderReview, "Received")]
    [InlineData(ProposalState.ClarificationRequested, "Received")]
    [InlineData(ProposalState.Revised, "Received")]
    [InlineData(ProposalState.Shortlisted, "Received")]
    [InlineData(ProposalState.NotSelected, "Received")]
    [InlineData(ProposalState.AwardOffered, "Received")]
    [InlineData(ProposalState.Awarded, "Received")]
    [InlineData(ProposalState.Declined, "No Quote")]
    [InlineData(ProposalState.Withdrawn, "No Quote")]
    [InlineData(ProposalState.Lapsed, "No Quote")]
    [InlineData(ProposalState.Cancelled, "No Quote")]
    public void Every_proposal_state_maps_to_one_of_the_ministrys_three(ProposalState state, string expected)
    {
        MinistryRfqFeedCsv.QuoteStatus(state).Should().Be(expected);
    }

    [Fact]
    public void Every_proposal_state_in_the_domain_is_covered_by_the_mapping()
    {
        var mapped = Enum.GetValues<ProposalState>().Select(s => MinistryRfqFeedCsv.QuoteStatus(s)).ToList();

        mapped.Should().OnlyContain(v => v == "Pending" || v == "Received" || v == "No Quote");
        Enum.GetValues<ProposalState>().Should().HaveCount(
            13,
            "a state added to the domain falls through this switch to Received, which reports a quote the "
            + "ministry never received - so a new one has to come back here");
    }

    [Fact]
    public void A_quote_that_lost_is_still_a_quote_that_was_received()
    {
        MinistryRfqFeedCsv.QuoteStatus(ProposalState.NotSelected).Should().Be("Received");
    }

    [Fact]
    public void The_total_carries_its_currency()
    {
        var cells = MinistryRfqFeedCsv.Cells(Record(ProposalState.Submitted, 7900.50m, "USD", "PRP-2026-000004"));

        cells[At("QuotationTotal")].Should().Be("7900.50");
        cells[At("QuotationCurrency")].Should().Be("USD");
        cells[At("QuotationNo")].Should().Be("PRP-2026-000004");
    }

    [Fact]
    public void A_quote_that_states_no_currency_takes_the_tenders()
    {
        var cells = MinistryRfqFeedCsv.Cells(
            Record(ProposalState.Submitted, 7900m, currency: null, proposalCode: "PRP-1", rfqCurrency: "SYP"));

        cells[At("QuotationCurrency")].Should().Be(
            "SYP",
            "a bid is priced in the currency the tender asked for unless the supplier says otherwise, and 68 of "
            + "73 priced proposals state none");
    }

    [Fact]
    public void A_row_with_no_total_carries_no_currency_either()
    {
        MinistryRfqFeedCsv.Cells(Record())[At("QuotationCurrency")].Should().BeNull(
            "a currency beside no amount is a column a reader has to explain away");
    }

    [Fact]
    public void The_total_is_rendered_to_two_decimal_places_as_their_sheet_specifies()
    {
        MinistryRfqFeedCsv.Cells(Record(ProposalState.Submitted, 7500.00000000m, "SYP", "PRP-1"))[At("QuotationTotal")]
            .Should().Be("7500.00");
        MinistryRfqFeedCsv.Cells(Record(ProposalState.Submitted, 0.0m, "SYP", "PRP-2"))[At("QuotationTotal")]
            .Should().Be("0.00");
    }

    [Fact]
    public void The_date_is_the_day_the_invitation_was_sent()
    {
        MinistryRfqFeedCsv.Cells(Record())[At("RFQDate")].Should().Be("2026-09-05");
    }
}
