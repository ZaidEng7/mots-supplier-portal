namespace MotsSupplierPortal.Application.Common;

/// <summary>
/// Task #16: the transport an OutboxMessage is actually handed to. The real ERP integration is
/// EPIC-23 (Domain/Common/OutboxMessage.cs's own doc comment: "not built here") - this interface
/// lets the durable dispatch job exist and be tested now, with delivery stubbed, the same shape
/// IEmailSender/LoggingEmailSender already uses for EPIC-15's not-yet-built email provider.
/// </summary>
public interface IOutboxTransport
{
    Task SendAsync(Guid messageId, string type, string payloadJson, CancellationToken ct = default);
}
