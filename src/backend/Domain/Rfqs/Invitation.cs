// The link between a tender and one invited supplier. It gates who may see the tender's detail and who
// may bid.
//
// It belongs to the tender rather than standing on its own, because it has no life independent of the
// tender it is part of.
//
// The statuses run Invited, then Viewed, then Responding, then Submitted, and a supplier may decline
// from any status before submitting.
//
// Responding and Submitted are driven by the bid itself, since they mean a bid has been started or
// handed in.

namespace MotsSupplierPortal.Domain.Rfqs;

public enum InvitationStatus
{
    Invited,
    Viewed,
    Responding,
    Submitted,
    Declined,
}

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
