// Feed 4 of the ministry's Syria Hotels Dashboard: one row per supplier per tender they were invited to.
//
// THE GRAIN IS THE INVITATION, not the tender and not the proposal. Their workbook says "one row per supplier
// per RFQ", and an invitation is exactly that: a tender crossed with a supplier who was asked. A supplier who
// was invited and never answered still has a row, because "we asked forty and eleven replied" is the fact the
// ministry is measuring, and a feed driven off proposals would quietly report only the eleven.
//
// QUOTESTATUS COMES FROM THE PROPOSAL AND NOT FROM THE INVITATION, which is the opposite of what the shape of
// the data suggests. Invitation carries a Status with Submitted and Declined among its values, and nothing in
// this product has ever written either: of 155 invitations, 148 still read Invited, including 59 where the
// supplier submitted and 4 where they went on to win the award. Reading it would report almost every supplier
// as not having replied. The proposal is the record that is actually maintained, so the proposal is what is
// read. That the invitation status is dead is a separate defect and is not fixed here.
//
// THE MAPPING TO THEIR THREE VALUES, and the two places it loses something:
//
//   Received    a quote arrived and entered evaluation - Submitted through Awarded, including the ones that
//               lost. NotSelected is a quote that was read and beaten, which is still a quote.
//   No Quote    Declined, Withdrawn, Lapsed, Cancelled. Four different endings, one word. "The supplier said
//               no", "they pulled it", "the window closed on their draft" and "the tender was withdrawn" are
//               genuinely different events, and their vocabulary has no room for the difference. The vendor
//               comment on the sheet says so.
//   Pending     a draft still being written, or no proposal at all. Both mean the question is still open.
//
// THE TOTAL IS COMPUTED FROM THE LINES, because that is where this product keeps it: a proposal stores no
// grand total, and ProposalItem.LineTotal is (quantity x price) minus any discount. Deriving it here rather
// than reading a stored figure is the same rule the comparison screen follows, and it is why a total can never
// drift from the lines it is made of.
//
// QUOTATIONCURRENCY IS A COLUMN THEIR SHEET DID NOT ASK FOR, and is sent anyway. Their feed 2 puts a currency
// beside every amount and their feed 4 does not, while proposals here are quoted in SYP or USD and this
// product holds no exchange rates. A column of bare numbers in two currencies is one a dashboard will sum into
// a figure that means nothing, and nobody would see it happen. An extra column costs their loader nothing.
//
// THE CURRENCY FALLS BACK TO THE TENDER'S when the proposal does not state one, which is almost always: of 73
// proposals, 5 carry a currency and all 45 tenders do. That is not a repair papering over missing data - a bid
// is priced in the currency the tender asked for unless the supplier says otherwise, so the tender's currency
// IS the bid's currency in the ordinary case. Without the fallback the column exists and is empty on 68 of the
// 73 rows that have a total, which is precisely the bare-number problem it was added to prevent.
//
// THE TOTAL IS RENDERED TO TWO DECIMAL PLACES because their sheet specifies decimal(14,2). The database column
// carries more scale than that, so an unformatted total arrives as 7500.00000000 beside another reading 0.0 -
// two different shapes of the same kind of number, in a column a loader will type.

namespace MotsSupplierPortal.Application.Rfqs;

using System.Globalization;
using MotsSupplierPortal.Application.Exports;
using MotsSupplierPortal.Domain.Proposals;

public sealed record MinistryRfqFeedRecord(
    string RfqReferenceCode,
    string SupplierReferenceCode,
    DateTimeOffset InvitedAt,
    string? ProposalReferenceCode,
    ProposalState? ProposalState,
    string? ProposalCurrencyCode,
    string RfqCurrencyCode,
    decimal? ProposalTotal,
    DateTimeOffset ModifiedAt);

public static class MinistryRfqFeedCsv
{
    public const string FileName = "mots-feed-rfqs.csv";

    public static readonly IReadOnlyList<string> Columns =
    [
        "RFQNo", "SupplierID", "RFQDate", "QuoteStatus", "QuotationNo", "QuotationTotal", "QuotationCurrency",
    ];

    public static string Header => CsvFormat.Row(Columns);

    public static string Row(MinistryRfqFeedRecord record) => CsvFormat.Row(Cells(record));

    public static IReadOnlyList<string?> Cells(MinistryRfqFeedRecord record) =>
    [
        record.RfqReferenceCode,
        record.SupplierReferenceCode,
        record.InvitedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        QuoteStatus(record.ProposalState),
        record.ProposalReferenceCode,
        record.ProposalTotal?.ToString("0.00", CultureInfo.InvariantCulture),
        record.ProposalTotal is null
            ? null
            : record.ProposalCurrencyCode ?? record.RfqCurrencyCode,
    ];

    public static string QuoteStatus(ProposalState? state) => state switch
    {
        null or Domain.Proposals.ProposalState.Draft => "Pending",
        Domain.Proposals.ProposalState.Declined
            or Domain.Proposals.ProposalState.Withdrawn
            or Domain.Proposals.ProposalState.Lapsed
            or Domain.Proposals.ProposalState.Cancelled => "No Quote",
        _ => "Received",
    };
}
