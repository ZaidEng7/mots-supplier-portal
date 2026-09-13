// One answer a supplier gave to one requirement on the tender. This is technical content, the other
// half of the two-envelope split.
//
// It is a real child table rather than the single JSON column the written data model describes. That
// is a deliberate deviation: the submission gate has to confirm every mandatory requirement has an
// answer, which needs each answer to be a row with a real link to its requirement rather than opaque
// JSON the domain would have to unpack and interpret.

namespace MotsSupplierPortal.Domain.Proposals;

public sealed class RequirementAnswer
{
    public Guid Id { get; init; }
    public Guid ProposalId { get; init; }
    public Guid RequirementId { get; init; }
    public string AnswerAr { get; internal set; } = null!;
    public string AnswerEn { get; internal set; } = null!;
}
