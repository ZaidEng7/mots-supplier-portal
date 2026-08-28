namespace MotsSupplierPortal.Application.Common;

/// <summary>
/// Writes append-only audit rows (FR-AUD-001/002).
///
/// Two things are deliberately NOT parameters:
///
/// - correlationId. It comes from IAuditContext. Every call site used to pass its own
///   Guid.NewGuid() - 51 of them - so the column was populated, indexed, and correlated to
///   nothing. Removing the parameter is what makes that impossible to reintroduce: a caller cannot
///   supply a fresh id because there is nowhere to put one.
/// - ipAddress. Same reason, plus it is a privacy decision that belongs in one place rather than
///   at 57 call sites (see HttpAuditContext for the truncation rationale).
///
/// This method does NOT save. The caller owns the transaction and must call SaveChangesAsync.
/// See AuditLogger for why that changed.
/// </summary>
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
        // DATABASE-MODEL.md §5 `changes` column: pre-built, pre-redacted JSON diff - build it
        // with AuditChangeBuilder, never pass raw field values directly.
        string? changes = null,
        CancellationToken ct = default);
}
