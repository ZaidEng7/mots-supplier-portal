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
        // DATABASE-MODEL.md §5 `changes` column: pre-built, pre-redacted JSON diff - build it
        // with AuditChangeBuilder, never pass raw field values directly.
        string? changes = null,
        CancellationToken ct = default);
}
