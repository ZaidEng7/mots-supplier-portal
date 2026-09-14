// One priced line on a bid, answering one line of the tender.
//
// This is the financial half of the two-envelope split: technical content is qualified before pricing
// is opened. It is deliberately its own table, owned by the bid but never included by any query that
// should see only the technical envelope.
//
// LineTotal is computed rather than stored. Totals are always derived from the lines and never taken
// from the client.

namespace MotsSupplierPortal.Domain.Proposals;

public sealed class ProposalItem
{
    public Guid Id { get; init; }
    public Guid ProposalId { get; init; }
    public Guid RfqItemId { get; init; }
    public decimal Quantity { get; internal set; }
    public decimal UnitPrice { get; internal set; }
    public decimal? Discount { get; internal set; }
    public int? LeadTimeDays { get; internal set; }
    public string? NotesAr { get; internal set; }
    public string? NotesEn { get; internal set; }

    public decimal LineTotal => (Quantity * UnitPrice) - (Discount ?? 0m);
}
