// The single way a state change asks for a notification.
//
// It adds an outbox row to the CALLER'S change tracker without saving, so the request commits with the state
// change or not at all. A handler that calls this and then throws has enqueued nothing, which is the entire
// point.
//
// The payload is validated HERE, at the point of construction, rather than when the dispatcher reads it. A
// payload that reaches the outbox has already been persisted, and the rule about what may appear in a
// notification is about data not being written down.
//
//
// ONE ROW PER RECIPIENT
//
// Most of the written transition tables name a GROUP, and one row per recipient is what makes read state
// per-person.
//
// The de-duplication key is suffixed with the recipient, so two people being told the same thing is two rows
// while one person being told twice is still one.

namespace MotsSupplierPortal.Infrastructure.Notifications;

using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class NotificationOutbox
{
    public static void Enqueue(
        AppDbContext db,
        string type,
        Guid recipientUserId,
        string dedupeKey,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        var payload = data ?? new Dictionary<string, string?>();
        NotificationPayload.Build(payload);

        var request = new NotificationRequest(type, recipientUserId, dedupeKey, new Dictionary<string, string?>(payload));

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = NotificationRequest.OutboxType,
            PayloadJson = request.ToPayloadJson(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    public static void EnqueueMany(
        AppDbContext db,
        string type,
        IEnumerable<Guid> recipientUserIds,
        string dedupeKeyPrefix,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        foreach (var recipient in recipientUserIds.Distinct())
        {
            Enqueue(db, type, recipient, $"{dedupeKeyPrefix}:{recipient}", data);
        }
    }
}
