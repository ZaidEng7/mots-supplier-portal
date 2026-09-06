namespace MotsSupplierPortal.Application.Common;

/// <summary>
/// Ambient provenance for audit rows (MSP-64): where the action came from, and which unit of work
/// it belonged to.
///
/// This exists so callers cannot supply these values. Every audit call site previously passed its
/// own <c>Guid.NewGuid()</c> as the correlation id - 51 of them - which meant the column was
/// populated and indexed while correlating to nothing. Supplying it from the ambient context is the
/// only way two audit rows written during one request can share an id, which is the entire point of
/// the field (FR-AUD-005, NFR-OBS-003).
/// </summary>
public interface IAuditContext
{
    /// <summary>Identifies the request or job run that produced the audit row. Stable for the
    /// lifetime of one unit of work, and equal to the distributed trace id when there is one, so an
    /// audit row can be joined to its trace.</summary>
    Guid CorrelationId { get; }

    /// <summary>
    /// Adopt a correlation id the CALLER supplied, so their log line and this request's audit rows carry
    /// the same value (EPIC-25's <c>Correlation-Id</c> echo).
    ///
    /// <para>On the interface rather than only on the HTTP implementation because the audit rows are
    /// written through this abstraction; a middleware that could set an id the audit layer does not read
    /// would echo a header and record something else. Implementations with no notion of an incoming
    /// request may ignore it - a job run has no caller to agree with.</para>
    /// </summary>
    void OverrideCorrelationId(Guid correlationId);

    /// <summary>Caller network provenance, or null when there is no request (background jobs).
    /// See the implementation for the truncation decision and why.</summary>
    string? IpAddress { get; }
}
