// Writes one audit row.
//
// Two things changed together here, with one theme: the row's provenance is no longer supplied by the
// caller, and the caller's transaction is no longer owned by this.
//
//
// WHY IT NO LONGER SAVES
//
// It used to commit for itself, and that is why a guarded update once answered a server error instead of a
// clean refusal: the audit write committed inside the caller's transaction, so the concurrency failure
// surfaced from the audit call rather than from the update the caller was prepared to catch.
//
// Persisting is the caller's job now, which is where the transaction boundary already was.
//
// Three callers had no save of their own and gained one. Without those, their audit rows would have been
// written to memory and dropped.

namespace MotsSupplierPortal.Infrastructure.Audit;

using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class AuditLogger(AppDbContext db, IAuditContext auditContext) : IAuditLogger
{
    public async Task LogAsync(
        string aggregateType,
        Guid aggregateId,
        string action,
        Guid? actorUserId = null,
        string? actorLabel = null,
        string? fromState = null,
        string? toState = null,
        string? reason = null,
        string? referenceCode = null,
        string? changes = null,
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
            Changes = changes,
            CorrelationId = auditContext.CorrelationId,
            IpAddress = auditContext.IpAddress,
        });

        await Task.CompletedTask;
    }
}
