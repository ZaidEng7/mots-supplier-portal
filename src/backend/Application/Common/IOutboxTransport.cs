// Where an outbox message is actually handed to.
//
// The real integration with the ministry's finance system is not built. This interface lets the durable
// dispatch job exist and be tested now with delivery stubbed, which is the same shape the email sender
// already uses for its own not-yet-built provider.

namespace MotsSupplierPortal.Application.Common;

public interface IOutboxTransport
{
    Task SendAsync(Guid messageId, string type, string payloadJson, CancellationToken ct = default);
}
