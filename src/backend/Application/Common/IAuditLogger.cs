namespace MotsSupplierPortal.Application.Common;

public interface IAuditLogger
{
    Task LogAsync(
        string aggregateType,
        Guid aggregateId,
        string action,
        Guid correlationId,
        Guid? actorUserId = null,
        string? actorLabel = null,
        string? fromState = null,
        string? toState = null,
        string? reason = null,
        string? referenceCode = null,
        CancellationToken ct = default);
}
