// One line in the compliance record: who did what to which record, and when.
//
// Rows are written in the same transaction as the change they describe, and application
// code never updates or deletes one. The table only grows.
//
// AuditActorKind says what kind of actor it was. User is a person, System is a background
// job, Integration is another system acting through the portal.
//
// FromState and ToState carry a state change where there was one. Changes carries a
// field-level before-and-after as JSON for the actions where a state change is not the
// whole story; it is null otherwise. That JSON is redacted before it is saved, because a
// request body can hold a price or a rejection reason and this table is read widely.
//
// CorrelationId ties every row produced by one request together, and it is the identifier
// the product shows the user when something fails.

namespace MotsSupplierPortal.Domain.Audit;

public enum AuditActorKind
{
    User,
    System,
    Integration,
}

public sealed class AuditLog
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Guid? ActorUserId { get; init; }
    public AuditActorKind ActorKind { get; init; }
    public string? ActorLabel { get; init; }
    public required string AggregateType { get; init; }
    public Guid AggregateId { get; init; }
    public string? ReferenceCode { get; init; }
    public required string Action { get; init; }
    public string? FromState { get; init; }
    public string? ToState { get; init; }
    public string? Reason { get; init; }
    public string? Changes { get; init; }
    public Guid CorrelationId { get; init; }
    public string? IpAddress { get; init; }
}
