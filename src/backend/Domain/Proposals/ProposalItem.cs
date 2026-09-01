namespace MotsSupplierPortal.Domain.Proposals;

/// <summary>FEAT-09.1/FR-PRP-002/DOMAIN-MODEL.md §5.5: a priced line answering one RfqItem - the
/// two-envelope FINANCIAL content (OQ-009 resolution: two-envelope evaluation, technical qualified
/// before financial is opened). Deliberately its own table (proposal.proposal_item), owned by
/// Proposal but never included by any query that should only see the technical envelope - see
/// Proposal.cs's own doc comment for the full separation reasoning. LineTotal is computed, never
/// stored - Totals are always derived from lines, never client-supplied (DOMAIN-MODEL.md §5.5's own
/// invariant).</summary>
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
