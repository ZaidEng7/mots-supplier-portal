// Turns one queued notification request into stored rows, one per recipient.
//
// It is the single place a notification row is written, which is why the rule about which notifications a
// person may switch off is enforced there rather than at each sender.

namespace MotsSupplierPortal.Application.Notifications;

public interface INotificationMaterialiser
{
    Task MaterialiseAsync(NotificationRequest request, CancellationToken ct = default);
}
