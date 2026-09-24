// Feed 4 as JSON, written from the same record the CSV is written from.
//
// THIS FEED NEEDS NO SEPARATE PROJECTION, unlike feed 1. Its record already arrives from the database as the
// values themselves - a reference code, a date, a state, a total - with only the mapping to their vocabulary
// and the currency fallback applied on the way out. Those two decisions live in the CSV class and are called
// from here, so there is still exactly one place that decides what a row means.
//
// THE NAMES ARE SPELLED OUT, because the web defaults would lower the first letter and their loader reads by
// name: RFQNo would go out as rfqNo.
//
// THE TOTAL IS A NUMBER, AND THAT IS THE DIFFERENCE WORTH HAVING. The CSV writes it to two decimal places
// because their sheet specifies decimal(14,2) and CSV has one type, so an unformatted total arrives as
// 7500.00000000 beside another reading 0.0. JSON carries the number itself and lets their loader keep the
// scale its own column declares.
//
// QUOTATIONCURRENCY IS STILL A COLUMN THEY DID NOT ASK FOR, for the reason the CSV explains: proposals here are
// quoted in SYP or USD, this product holds no exchange rates, and a column of bare numbers in two currencies is
// one a dashboard will sum into a figure that means nothing.

namespace MotsSupplierPortal.Application.Rfqs;

using System.Globalization;
using System.Text.Json.Serialization;

public sealed record MinistryRfqFeedJsonRow(
    [property: JsonPropertyName("RFQNo")] string RfqNo,
    [property: JsonPropertyName("SupplierID")] string SupplierId,
    [property: JsonPropertyName("RFQDate")] string RfqDate,
    [property: JsonPropertyName("QuoteStatus")] string QuoteStatus,
    [property: JsonPropertyName("QuotationNo")] string? QuotationNo,
    [property: JsonPropertyName("QuotationTotal")] decimal? QuotationTotal,
    [property: JsonPropertyName("QuotationCurrency")] string? QuotationCurrency);

public static class MinistryRfqFeedJson
{
    public static MinistryRfqFeedJsonRow Row(MinistryRfqFeedRecord record) => new(
        record.RfqReferenceCode,
        record.SupplierReferenceCode,
        record.InvitedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        MinistryRfqFeedCsv.QuoteStatus(record.ProposalState),
        record.ProposalReferenceCode,
        record.ProposalTotal,
        record.ProposalTotal is null
            ? null
            : record.ProposalCurrencyCode ?? record.RfqCurrencyCode);
}
