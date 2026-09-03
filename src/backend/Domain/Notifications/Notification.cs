using MotsSupplierPortal.Domain.Common;

namespace MotsSupplierPortal.Domain.Notifications;

/// <summary>DATABASE-MODEL.md §2.7's <c>notification_channel</c>.</summary>
public enum NotificationChannel
{
    InApp,
    Email,
}

/// <summary>DATABASE-MODEL.md §2.7's <c>notification_delivery_status</c>.</summary>
public enum NotificationDeliveryStatus
{
    Pending,
    Delivered,
    Failed,
}

/// <summary>
/// A persistent, per-recipient notification — the in-app channel T3-14 says does not exist, and the
/// aggregate DESIGN-SYSTEM.md §6.14's bell panel "mirrors".
///
/// <para><b>Not a toast.</b> §6.14 draws the line: a toast is transient, lives in the client, and
/// auto-dismisses; a notification is persistent, per-recipient, and has read state. Nothing here is
/// ever produced by a toast, and nothing here auto-dismisses.</para>
///
/// <para><b>Bilingual at rest, not at render.</b> Title and body are stored in both languages
/// because UX-WRITING.md §10 requires delivery "bilingual per the user's locale" and the same row
/// is read by the bell, by the centre, and (later) by an email channel. Storing one language and
/// translating on read would mean a notification whose wording depends on when it was opened.</para>
///
/// <para><b>BRULE-091: no personal or sensitive data in the payload.</b> <see cref="DataJson"/>
/// carries only what a deep link needs, and which keys those are is an allow-list enforced by
/// <c>NotificationPayload</c> - not a convention.</para>
/// </summary>
public sealed class Notification : IVersionedAggregate
{
    public Guid Id { get; init; }

    /// <summary>§2.7's <c>recipient_user_id</c> FK. Also the row-scope key: §10 requires that a
    /// notification never leaks across scope, and this column is what makes that a WHERE clause
    /// rather than a policy.</summary>
    public required Guid RecipientUserId { get; init; }

    /// <summary>The event kind, e.g. <c>rfq.published</c>. Stable, machine-readable, and the key the
    /// copy catalogue is keyed by.</summary>
    public required string Type { get; init; }

    public NotificationChannel Channel { get; init; } = NotificationChannel.InApp;

    public required string TitleAr { get; init; }
    public required string TitleEn { get; init; }
    public required string BodyAr { get; init; }
    public required string BodyEn { get; init; }

    /// <summary>§2.7's <c>data jsonb</c>. Deep-link identifiers only - see BRULE-091 above.</summary>
    public string DataJson { get; init; } = "{}";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; private set; }

    public NotificationDeliveryStatus DeliveryStatus { get; set; } = NotificationDeliveryStatus.Delivered;

    /// <summary>
    /// §2.7's <c>dedupe_key</c>, with <c>U(dedupe_key)</c>. The idempotency guarantee: the same event
    /// delivered twice produces one row, because the second INSERT loses to the unique index rather
    /// than being suppressed by a check-then-write race.
    /// </summary>
    public required string DedupeKey { get; init; }

    /// <summary>§8.1's version. See the report for why marking-read still carries a precondition.</summary>
    public uint RowVersion { get; private set; }

    public bool IsRead => ReadAt is not null;

    /// <summary>
    /// Idempotent by design: marking an already-read notification read again is not an error and does
    /// not move the timestamp. A bell that marks on open would otherwise rewrite "when you first saw
    /// this" every time the panel rendered.
    /// </summary>
    public void MarkRead(DateTimeOffset now)
    {
        ReadAt ??= now;
    }

    public void MarkUnread() => ReadAt = null;
}
