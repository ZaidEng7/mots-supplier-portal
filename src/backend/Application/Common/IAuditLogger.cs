// Writes one append-only audit row.
//
// Two things are deliberately not parameters.
//
// The correlation identifier comes from the ambient context. Every call site used to pass its own freshly
// generated one, fifty-one of them, so the column was populated, indexed, and correlated to nothing.
// Removing the parameter is what makes that impossible to reintroduce: a caller cannot supply a fresh
// identifier because there is nowhere to put one.
//
// The network address is the same, and it is also a privacy decision that belongs in one place rather than
// at fifty-seven call sites.
//
// This does not save. The caller owns the transaction and must commit it.

namespace MotsSupplierPortal.Application.Common;

public interface IAuditLogger
{
    Task LogAsync(
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
        CancellationToken ct = default);
}
