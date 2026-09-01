namespace MotsSupplierPortal.Domain.Rfqs;

/// <summary>FEAT-08.1/FR-INV-001: Invited -> Viewed -> Responding -> Submitted, or Declined from
/// any pre-Submitted status. Responding/Submitted are driven from EPIC-09 (Proposal) once that
/// aggregate exists - nothing in this build's Rfq aggregate transitions an invitation past Viewed
/// except Decline, since there is no Proposal yet to start one from.</summary>
public enum InvitationStatus
{
    Invited,
    Viewed,
    Responding,
    Submitted,
    Declined,
}

/// <summary>FEAT-08.1/FR-INV-001, DOMAIN-MODEL.md §5.4/DATABASE-MODEL.md §2.4: the link between an
/// RFQ and a specific invited Supplier - gates who may view RFQ detail (FEAT-08.6) and, later,
/// propose (EPIC-09). A child entity of Rfq, not its own aggregate: it has no lifecycle independent
/// of the RFQ it belongs to.</summary>
public sealed class Invitation
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public Guid SupplierId { get; init; }
    public InvitationStatus Status { get; internal set; } = InvitationStatus.Invited;
    public DateTimeOffset InvitedAt { get; init; }
    public DateTimeOffset? ViewedAt { get; internal set; }
    public DateTimeOffset? RespondedAt { get; internal set; }
    public string? DeclineReason { get; internal set; }
}
