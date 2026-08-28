using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Audit;

/// <summary>
/// MSP-64. Two changes with one theme: the audit row's provenance is no longer supplied by the
/// caller, and the caller's transaction is no longer owned by the audit write.
///
/// This used to call SaveChangesAsync itself. That is why MSP-65's guarded UPDATE returned 500
/// instead of 409: the audit write committed inside the caller's transaction, so the concurrency
/// exception surfaced from the audit call rather than from the guarded update the caller was
/// prepared to catch. Persisting is now the caller's job, which is where the transaction boundary
/// already lived.
///
/// Three callers had no SaveChangesAsync of their own and now do
/// (GetDocumentDownloadUrlHandler, InviteSupplierUserHandler, MfaHandlers); without them their
/// audit rows would simply never be written - an audit trail silently losing entries, which is
/// worse than one that never existed because people rely on it.
/// </summary>
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

        // Deliberately no SaveChangesAsync - see the class comment. `ct` is kept on the signature
        // because implementations of IAuditLogger may need it and removing it would churn all 57
        // call sites for nothing.
        await Task.CompletedTask;
    }
}
