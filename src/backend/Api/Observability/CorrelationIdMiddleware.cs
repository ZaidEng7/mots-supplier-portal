// Reads the caller's own correlation identifier, echoes it back, and makes it the identifier every
// audit row and every failure response for this request carries.
//
// Every audit row and failure response already carried one, derived from the trace, but the request
// header was never read. A client that sent its own identifier got a different one back and could not
// join its log line to the server's, which is the entire purpose of sending one. The framework does
// pick up the standard tracing header automatically, but that is a different header in a different
// format, and not the one somebody integrating against this API would think to send.
//
// A value that cannot be parsed is ignored rather than carried. An identifier a caller cannot match
// back is worse than a generated one, because it looks like correlation and joins nothing. Anything
// unparseable is dropped and the trace-derived identifier stands, and the response then echoes that, so
// a caller who sent rubbish can see from the reply that their value was not used.
//
// An all-zeroes identifier is refused alongside unparseable ones. It is what a client sends when its
// own identifier was never set, and treating it as real would join every such request to every other.
//
// A supplied identifier is also attached to the trace, so it reaches the logs and any downstream system
// rather than only the audit table. It is attached as a label rather than replacing the trace
// identifier, which the tracing system owns; rewriting that would break the hierarchy this request is
// already part of.
//
// The header is echoed on every response, not only on failures. A client reconciling a successful
// write, which is the case that matters when two systems compare their records of one submission, needs
// the identifier just as much as one reading a failure.
//
// The echo is registered before the response starts, because headers cannot be added once it has, and
// it has to fire for a server error written further up the pipeline as much as for a success.

namespace MotsSupplierPortal.Api.Observability;

using System.Diagnostics;
using MotsSupplierPortal.Application.Common;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Correlation-Id";

    public async Task InvokeAsync(HttpContext context, IAuditContext auditContext)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var supplied)
            && Guid.TryParse(supplied.ToString(), out var correlationId)
            && correlationId != Guid.Empty)
        {
            auditContext.OverrideCorrelationId(correlationId);

            Activity.Current?.SetTag("correlation.id", correlationId.ToString());
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = auditContext.CorrelationId.ToString();
            return Task.CompletedTask;
        });

        await next(context);
    }
}
