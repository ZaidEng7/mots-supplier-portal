// One notification, as it travels through the outbox.
//
//
// WHY THE OUTBOX AND NOT A DIRECT WRITE
//
// A notification caused by a state change is written in the same transaction as that change. So it cannot
// fire for a change that rolled back, and it cannot be lost if the process dies between committing and
// queueing.
//
// On a tender, "the supplier was told they were shortlisted but the shortlisting did not commit" is a
// dispute, and so is its mirror image.
//
//
// THE CONSTRAINT IN THE OTHER DIRECTION
//
// Writing the row is inside the transaction. Delivering it is not.
//
// A delivery failure must never roll back a committed award, so the dispatcher's failures mark the message
// failed and stop there.

namespace MotsSupplierPortal.Application.Notifications;

using System.Text.Json;

public sealed record NotificationRequest(
    string Type,
    Guid RecipientUserId,
    string DedupeKey,
    Dictionary<string, string?> Data)
{
    public const string OutboxType = "notification";

    public string ToPayloadJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    public static NotificationRequest FromPayloadJson(string json) =>
        JsonSerializer.Deserialize<NotificationRequest>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("Notification outbox payload could not be read.");
}
