// One notification sitting in one person's bell panel, kept until they read it.
//
// Not a toast. A toast is transient, lives in the browser, and disappears on its own. A
// notification is stored, belongs to one recipient, and has a read state. Nothing here is ever
// produced by a toast, and nothing here disappears on its own.
//
// Title and body are stored in both languages rather than translated when read. The same row is
// read by the bell, by the notification centre, and later by email, and the wording must be the
// same in all three. Storing one language and translating on read would mean a notification whose
// words depend on when it was opened.
//
// DataJson carries only the identifiers a link needs, never personal or sensitive data. Which keys
// are allowed is a list enforced in code rather than a convention people are asked to follow.
//
// RecipientUserId is also what keeps notifications from crossing between accounts. Scoping to one
// recipient is a condition on the query rather than a rule somebody has to remember.
//
// Type is the event kind, such as rfq.published. It is stable, machine-readable, and the key the
// wording catalogue is keyed by.
//
// DedupeKey is unique in the database, and that is what makes delivery safe to retry: the same
// event delivered twice produces one row, because the second insert loses to the unique index. A
// check-then-write in code would leave a race between the check and the write.
//
// MarkRead is deliberately idempotent. Marking an already-read notification read again is not an
// error and does not move the timestamp, because a bell that marks on open would otherwise rewrite
// "when you first saw this" every time the panel rendered.
//
// RowVersion's setter is never called from this class. It exists so the database layer can put the
// stored version back, which is why it is private.

namespace MotsSupplierPortal.Domain.Notifications;

using MotsSupplierPortal.Domain.Common;

public enum NotificationChannel
{
    InApp,
    Email,
}

public enum NotificationDeliveryStatus
{
    Pending,
    Delivered,
    Failed,
}

public sealed class Notification : IVersionedAggregate
{
    public Guid Id { get; init; }

    public required Guid RecipientUserId { get; init; }

    public required string Type { get; init; }

    public NotificationChannel Channel { get; init; } = NotificationChannel.InApp;

    public required string TitleAr { get; init; }
    public required string TitleEn { get; init; }
    public required string BodyAr { get; init; }
    public required string BodyEn { get; init; }

    public string DataJson { get; init; } = "{}";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; private set; }

    public NotificationDeliveryStatus DeliveryStatus { get; set; } = NotificationDeliveryStatus.Delivered;

    public required string DedupeKey { get; init; }

    public uint RowVersion { get; private set; }

    public bool IsRead => ReadAt is not null;

    public void MarkRead(DateTimeOffset now)
    {
        ReadAt ??= now;
    }

    public void MarkUnread() => ReadAt = null;
}
