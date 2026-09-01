namespace MotsSupplierPortal.Domain.Proposals;

/// <summary>FEAT-09.2/FR-PRP-003: one answer in the TechnicalResponse against an RFQ Requirement -
/// two-envelope TECHNICAL content. A real child table rather than the DATABASE-MODEL.md §2.4
/// documented `technical_response jsonb` column - deliberate deviation, not an oversight: FEAT-09.5's
/// submission gate must verify every MANDATORY Requirement has an answer, which needs each answer to
/// be a real, queryable row (RequirementId FK) rather than opaque JSON the domain would otherwise
/// have to deserialize and interpret. See Proposal.cs's own doc comment for the rest of the
/// two-envelope shape.</summary>
public sealed class RequirementAnswer
{
    public Guid Id { get; init; }
    public Guid ProposalId { get; init; }
    public Guid RequirementId { get; init; }
    public string AnswerAr { get; internal set; } = null!;
    public string AnswerEn { get; internal set; } = null!;
}
