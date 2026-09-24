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
//
//
// WHAT KIND OF ACTOR IT WAS
//
// A person is User, another system holding an API key is Integration, and anything else is System. The caller
// does not say which, and cannot: a handler serving a feed pull is the same handler either way, and asking it
// to know would mean the answer depended on every call site remembering.
//
// Integration was a value the enum carried and nothing ever wrote. Every feed pull recorded itself as System,
// the actor a background job inside this product uses, so the audit trail could not distinguish our own nightly
// work from somebody else's dashboard reading the national registry - which is the one distinction anyone
// auditing an integration needs.
//
// A user identifier wins over a key, rather than the two being combined. Both cannot be true of one request,
// and if they somehow were, the person is the accountable actor.

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
            ActorKind = ActorKindOf(actorUserId, auditContext.IntegrationLabel),
            ActorLabel = actorLabel ?? auditContext.IntegrationLabel,
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

    private static AuditActorKind ActorKindOf(Guid? actorUserId, string? integrationLabel) => (actorUserId, integrationLabel) switch
    {
        (not null, _) => AuditActorKind.User,
        (null, not null) => AuditActorKind.Integration,
        _ => AuditActorKind.System,
    };
}
