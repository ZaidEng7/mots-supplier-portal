namespace MotsSupplierPortal.Domain.Rfqs;

/// <summary>FEAT-10.2/FR-CLR-002, OQ-008 interim decision (ASM-044): "Private by default with an
/// option to publish to all" - the opposite default from FEAT-10.2's own story text ("default
/// publish for fairness [ASSUMPTION]"). Built against OQ-008 since it is the more recent,
/// explicitly-tracked open question with a recorded interim decision, not a bare assumption buried
/// in a user story; see Clarification.Answer's own doc comment and the session report for the full
/// reconciliation.</summary>
public enum ClarificationVisibility
{
    PrivateToAsker,
    PublishedToAll,
}

/// <summary>FEAT-10.1..10.3/FR-CLR-001..003, DOMAIN-MODEL.md §5.4: a threaded Q&A item on an RFQ.
/// A child entity of Rfq, not its own aggregate - same shape as Invitation.
///
/// <para><b>Anonymization is a display-layer concern, not a data-layer one.</b> AskedBySupplierId
/// is always stored and always visible to the buyer (audit needs it); it is simply never included
/// in the DTO served back to a supplier who is not the asker viewing a PublishedToAll clarification
/// - see RfqDtoMapper.ToSupplierClarificationDto. This entity never strips or nulls the real
/// asker.</para></summary>
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
