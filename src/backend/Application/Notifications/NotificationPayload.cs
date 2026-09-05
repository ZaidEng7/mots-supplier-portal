using System.Text.Json;
using System.Text.Json.Nodes;

namespace MotsSupplierPortal.Application.Notifications;

/// <summary>
/// BRULE-091's gate: "No personal or sensitive data in notification payloads."
///
/// <para><b>Why an allow-list rather than a deny-list.</b> The failure this prevents is not someone
/// deliberately putting an email address in a notification - it is someone adding a field for a deep
/// link six months from now and including a price, a supplier's contact, or a rejection reason
/// alongside it. A deny-list only catches the sensitive things somebody already thought of. An
/// allow-list makes adding ANY key a decision: the key is either listed here, with someone having
/// looked at it, or the test is red.</para>
///
/// <para>The list is deliberately narrow: identifiers and public reference codes, which is what a
/// deep link needs and nothing more. The notification's own words carry the meaning, and those are
/// authored copy rather than data.</para>
/// </summary>
public static class NotificationPayload
{
    /// <summary>
    /// Every key permitted in <c>notification.data</c>. Adding one is a deliberate act - and the
    /// coverage test fails in BOTH directions, so a key listed here that nothing emits is also red.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        // Public reference codes - already visible to the recipient on the screen the link opens.
        "rfqCode",
        "proposalCode",
        "supplierCode",

        // Opaque identifiers, used for routing only.
        "rfqId",
        "proposalId",
        "awardId",
        "evaluationId",
        "notificationId",

        // Where the deep link should land. A route, never content.
        //
        // T-018 tried to add "submissionDeadline" here and the gate refused it - correctly. The date
        // is CONTENT, and this list's own rule is that content belongs in the authored copy or behind
        // the link. The deadline-change bodies say the deadline moved and point at the RFQ, which is
        // the same treatment award.rejected gives a rejection reason and rfq.clarification_requested
        // gives its details. Widening the list to make copy read better is exactly the accident it
        // exists to prevent.
        "route",
    };

    /// <summary>Keys present in the payload that nobody has approved.</summary>
    public static IReadOnlyList<string> DisallowedKeysIn(string dataJson)
    {
        if (JsonNode.Parse(dataJson) is not JsonObject data) return [];

        return [.. data.Select(pair => pair.Key).Where(key => !AllowedKeys.Contains(key)).Order()];
    }

    /// <summary>
    /// Builds a payload, refusing anything unlisted at the point of construction rather than at the
    /// point of reading. A leak that reaches the database has already happened.
    /// </summary>
    public static string Build(IReadOnlyDictionary<string, string?> values)
    {
        var rejected = values.Keys.Where(key => !AllowedKeys.Contains(key)).Order().ToList();
        if (rejected.Count > 0)
        {
            throw new InvalidOperationException(
                $"BRULE-091: notification payload keys not on the allow-list: {string.Join(", ", rejected)}. " +
                "Add the key to NotificationPayload.AllowedKeys deliberately, or leave it out.");
        }

        var data = new JsonObject();
        foreach (var (key, value) in values.Where(v => v.Value is not null))
        {
            data[key] = value;
        }

        return data.ToJsonString(JsonSerializerOptions.Web);
    }
}
