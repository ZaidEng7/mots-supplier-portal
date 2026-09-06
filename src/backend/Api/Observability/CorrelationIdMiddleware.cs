using System.Diagnostics;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Api.Observability;

/// <summary>
/// Reads the caller's <c>Correlation-Id</c>, echoes it back, and makes it the id every audit row and
/// problem response for this request carries.
///
/// <para><b>The gap this closes.</b> Every audit row and every problem response already carried a
/// correlation id - <see cref="Authorization.HttpAuditContext"/> reinterprets the W3C trace id as a Guid -
/// but the REQUEST header was never read. A client that sent <c>Correlation-Id: X</c> got a different id
/// back and could not join its own log line to the server's, which is the entire purpose of sending one.
/// ASP.NET does pick up <c>traceparent</c> automatically, but that is a different header in a different
/// format, and not the one a caller integrating against this API would think to send.</para>
///
/// <para><b>A malformed value is ignored, not carried.</b> An id a caller cannot parse back is worse than
/// a generated one: it looks like correlation and joins nothing. Anything that is not a plain Guid is
/// dropped and the trace-derived id stands - and the response then echoes THAT, so a caller who sent
/// rubbish can see from the reply that their value was not used.</para>
///
/// <para><b>The header is echoed on every response, not only on errors.</b> A client correlating a
/// successful write - the case that matters when reconciling two systems' records of one submission -
/// needs the id just as much as one reading a failure.</para>
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Correlation-Id";

    public async Task InvokeAsync(HttpContext context, IAuditContext auditContext)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var supplied)
            && Guid.TryParse(supplied.ToString(), out var correlationId)
            && correlationId != Guid.Empty)
        {
            // Guid.Empty is refused alongside unparseable values: it is what a client sends when its own
            // id was never set, and treating "all zeroes" as a real correlation id would join every such
            // request to every other.
            auditContext.OverrideCorrelationId(correlationId);

            // Also onto the Activity, so the id reaches the logs and any downstream trace rather than only
            // the audit table. A tag rather than a replacement of the trace id: the trace id is 16 bytes
            // the tracing system owns, and rewriting it would break the span hierarchy this request is
            // already part of.
            Activity.Current?.SetTag("correlation.id", correlationId.ToString());
        }

        // Registered before the response starts, because headers cannot be added once it has - and this
        // must fire for a 500 written by the pipeline above as much as for a 200.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = auditContext.CorrelationId.ToString();
            return Task.CompletedTask;
        });

        await next(context);
    }
}
