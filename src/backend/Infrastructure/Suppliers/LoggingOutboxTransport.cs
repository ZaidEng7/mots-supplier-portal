// The stand-in transport for development: it records that a dispatch happened instead of delivering it.
//
// Swapped for the real integration when that lands. The durable dispatcher job around it does not change,
// which is the same shape the logging email sender has for its not-yet-built provider.
//
// The payload is logged as a length rather than as content, for the same reason the logging email sender
// never logs a body. Payloads carry supplier names and reference codes, and this is the one production
// log-writing call site in the outbox path.
//
// The type and the message identifier are enough to trace which event a line is about without putting the
// payload into the log stream.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;

public sealed class LoggingOutboxTransport(ILogger<LoggingOutboxTransport> logger) : IOutboxTransport
{
    public Task SendAsync(Guid messageId, string type, string payloadJson, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Outbox message {OutboxMessageId} dispatched | type {OutboxMessageType} | {PayloadLength} chars",
            messageId, type, payloadJson.Length);

        return Task.CompletedTask;
    }
}
