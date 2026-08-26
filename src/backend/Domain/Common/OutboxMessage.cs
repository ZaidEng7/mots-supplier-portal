namespace MotsSupplierPortal.Domain.Common;

public enum OutboxSyncStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>
/// Transactional bridge for domain/integration events (FEAT-03.5, docs/architecture/DOMAIN-MODEL.md
/// §5.3/§Shared-kernel). Written in the SAME transaction as the state change it represents, so
/// approval is never lost even if the eventual ERP dispatcher (EPIC-23, not built here) is down -
/// the portal never blocks on ERP.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public required string Type { get; init; }
    public required string PayloadJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public OutboxSyncStatus SyncStatus { get; set; } = OutboxSyncStatus.Pending;
    public DateTimeOffset? ProcessedAt { get; set; }
}
