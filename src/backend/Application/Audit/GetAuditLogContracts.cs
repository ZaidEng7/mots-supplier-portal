namespace MotsSupplierPortal.Application.Audit;

public sealed record AuditLogEntryDto(
    DateTimeOffset OccurredAt,
    string AggregateType,
    Guid AggregateId,
    string Action,
    string? FromState,
    string? ToState,
    string? ActorLabel);

public interface IGetAuditLogHandler
{
    /// <summary>Gated by audit.read (STORY-01.7.1); append-only, read-only surface.</summary>
    Task<IReadOnlyList<AuditLogEntryDto>> HandleAsync(Guid aggregateId, CancellationToken ct);
}
