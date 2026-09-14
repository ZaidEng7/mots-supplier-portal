// Builds the before-and-after difference an audit row records, as JSON.
//
// Its list of sensitive field names is shared with the log-redaction stage by calling into it rather than
// keeping a parallel copy, so the two cannot drift apart.
//
// Matching is on the field name containing one of those words, and it is a backstop only. A caller that
// already knows a value is sensitive, such as a bank account number, must pass it already masked rather
// than relying on this: the list recognises a name containing the word for an international account number
// and would not recognise a differently spelled one.
//
// Only fields whose before and after actually differ are included, and nothing is written at all when
// nothing differs, so a save that changed nothing does not leave an audit row implying it did.

namespace MotsSupplierPortal.Infrastructure.Audit;

using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Observability;

internal static class AuditChangeBuilder
{
    public static string? Build(params (string Field, object? Before, object? After)[] fields)
    {
        var diff = new Dictionary<string, object?>();
        foreach (var (field, before, after) in fields)
        {
            if (Equals(before, after)) continue;

            var isSensitive = RedactingEnricher.IsSensitiveName(field);
            var redacted = RedactingEnricher.RedactedPlaceholder;
            diff[field] = new { before = isSensitive ? redacted : before, after = isSensitive ? redacted : after };
        }

        return diff.Count == 0 ? null : JsonSerializer.Serialize(diff);
    }
}
