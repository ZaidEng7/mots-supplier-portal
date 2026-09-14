// An amendment issued against a published tender: a change to the specification or the timeline,
// recorded as its own entry.
//
// This is the first real use of the rule that a tender is locked once published, except for addenda. A
// published tender's items, requirements and basics stay locked; an addendum is a separate additive
// record of a change rather than an edit of the original content.
//
// The written rule leaves open exactly what a material amendment requires beyond notifying the
// invitees, so this implements the confirmed half, recording the change and telling people, and not a
// speculative difference-and-versioning system for the half that is undecided.

namespace MotsSupplierPortal.Domain.Rfqs;

public sealed class Addendum
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public string TitleAr { get; init; } = null!;
    public string TitleEn { get; init; } = null!;
    public string DescriptionAr { get; init; } = null!;
    public string DescriptionEn { get; init; } = null!;
    public DateTimeOffset IssuedAt { get; init; }
    public Guid IssuedByUserId { get; init; }
}
