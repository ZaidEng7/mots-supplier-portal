using System.Text.Json;

namespace MotsSupplierPortal.Application.Notifications;

/// <summary>
/// One notification, as it travels through the Outbox.
///
/// <para><b>Why the Outbox and not a direct write (D-5).</b> A notification caused by a state change
/// is written in the SAME transaction as that change, so it cannot fire for a change that rolled
/// back, and cannot be lost if the process dies between commit and enqueue. In a tender, "the
/// supplier was told they were shortlisted but the shortlisting didn't commit" is a dispute, and so
/// is its mirror image.</para>
///
/// <para><b>BRULE-099 is the constraint the other way.</b> Writing the row is inside the
/// transaction; DELIVERING it is not. A delivery failure must never roll back a committed award, so
/// the dispatcher's failures mark the message Failed and stop there.</para>
/// </summary>
public sealed record NotificationRequest(
    string Type,
    Guid RecipientUserId,
    string DedupeKey,
    Dictionary<string, string?> Data)
{
    /// <summary>The Outbox message type this travels under. The dispatcher switches on it.</summary>
    public const string OutboxType = "notification";

    public string ToPayloadJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    public static NotificationRequest FromPayloadJson(string json) =>
        JsonSerializer.Deserialize<NotificationRequest>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("Notification outbox payload could not be read.");
}
