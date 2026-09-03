namespace MotsSupplierPortal.Application.Notifications;

/// <summary>Turns an Outbox notification request into a persisted, per-recipient row.</summary>
public interface INotificationMaterialiser
{
    Task MaterialiseAsync(NotificationRequest request, CancellationToken ct = default);
}
