namespace MotsSupplierPortal.Domain.Audit;

public enum AuditActorKind
{
    User,
    System,
    Integration,
}

/// <summary>
/// Append-only compliance record (docs/architecture/DATABASE-MODEL.md §5). Written in the same
/// unit of work as the state change it records; never updated or deleted by application code.
/// </summary>
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
    public Guid CorrelationId { get; init; }
    public string? IpAddress { get; init; }
}
