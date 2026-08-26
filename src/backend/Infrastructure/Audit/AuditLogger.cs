using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Audit;

public sealed class AuditLogger(AppDbContext db) : IAuditLogger
{
    public async Task LogAsync(
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
        CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = actorUserId,
            ActorKind = actorUserId is null ? AuditActorKind.System : AuditActorKind.User,
            ActorLabel = actorLabel,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            ReferenceCode = referenceCode,
            Action = action,
            FromState = fromState,
            ToState = toState,
            Reason = reason,
            CorrelationId = correlationId,
        });

        await db.SaveChangesAsync(ct);
    }
}
