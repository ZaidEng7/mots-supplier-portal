using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>
/// Dev-only stand-in transport: logs that a dispatch happened instead of delivering to ERP.
/// Swapped for the real EPIC-23 integration when it lands; the durable OutboxDispatcher job around
/// it does not change - same shape as LoggingEmailSender for EPIC-15's not-yet-built provider.
///
/// Task #16/BRULE-091: payload is logged as a length, not its content, for the same reason
/// LoggingEmailSender never logs an email body - PayloadJson carries supplier names and reference
/// codes (ReviewApplicationHandlers.cs, ComplianceReTrigger.cs), and this is the one production
/// log-writing call site in the outbox path. Type and message id are enough to trace which event
/// this line is about without putting the payload's content into the log stream.
/// </summary>
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
