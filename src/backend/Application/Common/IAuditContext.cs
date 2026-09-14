// Where an audit row came from: which request or job run produced it, and the caller's network address.
//
// It exists so that callers cannot supply these values. Every audit call site used to pass its own freshly
// generated correlation identifier, fifty-one of them, which meant the column was populated and indexed
// while correlating to nothing at all.
//
// Taking it from the ambient context is the only way two audit rows written during one request can share
// an identifier, which is the entire point of the field.
//
// The identifier is stable for one unit of work and equal to the distributed trace identifier when there
// is one, so an audit row can be joined to its trace.
//
// OverrideCorrelationId adopts an identifier the caller supplied, so their own log line and this request's
// audit rows carry the same value.

namespace MotsSupplierPortal.Application.Common;

public interface IAuditContext
{
    Guid CorrelationId { get; }

    void OverrideCorrelationId(Guid correlationId);

    string? IpAddress { get; }
}
