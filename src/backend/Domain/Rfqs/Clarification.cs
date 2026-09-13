// A question a supplier asked about a tender, and the buyer's answer. It belongs to the tender, the
// same way an invitation does.
//
// Visibility decides who sees the answer: only the supplier who asked, or every invited supplier.
//
// The default is private with an option to publish. That is the opposite of the default written into
// one user story, which assumed publishing everything for fairness. The private default comes from the
// tracked open question about this feature, which carries a recorded interim decision, and a recorded
// decision outranks an assumption buried in a story.
//
// Anonymity is handled where the answer is displayed, not here. The supplier who asked is always
// stored and always visible to the buyer, because an audit needs it. It is simply left out of what is
// sent back to a supplier who is not the asker reading a published answer. This record never strips or
// blanks the real asker.

namespace MotsSupplierPortal.Domain.Rfqs;

public enum ClarificationVisibility
{
    PrivateToAsker,
    PublishedToAll,
}

public sealed class Clarification
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public Guid AskedBySupplierId { get; init; }
    public string Question { get; init; } = null!;
    public string? Answer { get; internal set; }
    public ClarificationVisibility Visibility { get; internal set; } = ClarificationVisibility.PrivateToAsker;
    public DateTimeOffset AskedAt { get; init; }
    public DateTimeOffset? AnsweredAt { get; internal set; }
}
