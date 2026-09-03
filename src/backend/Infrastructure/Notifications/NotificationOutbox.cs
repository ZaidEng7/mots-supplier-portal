using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Notifications;

/// <summary>
/// The single way a state change asks for a notification (D-5).
///
/// <para>Adds an Outbox row to the CALLER'S change tracker without saving - so it commits with the
/// state change or not at all. A handler that calls this and then throws has enqueued nothing, which
/// is the entire point.</para>
/// </summary>
public static class NotificationOutbox
{
    public static void Enqueue(
        AppDbContext db,
        string type,
        Guid recipientUserId,
        string dedupeKey,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        // Validated HERE, at the point of construction, rather than when the dispatcher reads it.
        // A payload that reaches the outbox has already been persisted, and BRULE-091 is about data
        // not being written down.
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

    /// <summary>
    /// Enqueues one notification per recipient, de-duplicated by user.
    ///
    /// <para>Most of BUSINESS-PROCESSES' transition tables name a GROUP - "in-app to invitees",
    /// "in-app to committee" - and one row per recipient is what makes read state per-person. The
    /// dedupe key is suffixed with the recipient, so two people being told the same thing is two
    /// rows, while one person being told twice is still one.</para>
    /// </summary>
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
