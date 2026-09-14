// The gate that keeps personal and sensitive data out of notification payloads.
//
// It is an allow-list of keys rather than a list of forbidden ones.
//
//
// WHY AN ALLOW-LIST
//
// The failure this prevents is not somebody deliberately putting an email address in a notification. It is
// somebody adding a field for a link six months from now and including a price, a supplier's contact or a
// rejection reason alongside it.
//
// A list of forbidden things only catches what somebody already thought of. An allow-list makes adding any
// key a decision: either the key is listed here, with somebody having looked at it, or the test fails.
//
// The list is deliberately narrow. Identifiers and public reference codes, which is what a link needs and
// nothing more. The notification's own words carry the meaning, and those are authored wording rather than
// data.

namespace MotsSupplierPortal.Application.Notifications;

using System.Text.Json;
using System.Text.Json.Nodes;

public static class NotificationPayload
{
    public static readonly IReadOnlySet<string> AllowedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "rfqCode",
        "proposalCode",
        "supplierCode",

        "rfqId",
        "proposalId",
        "awardId",
        "evaluationId",
        "notificationId",

        "route",
    };

    public static IReadOnlyList<string> DisallowedKeysIn(string dataJson)
    {
        if (JsonNode.Parse(dataJson) is not JsonObject data) return [];

        return [.. data.Select(pair => pair.Key).Where(key => !AllowedKeys.Contains(key)).Order()];
    }

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
