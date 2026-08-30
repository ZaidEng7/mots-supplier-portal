using System.Globalization;
using System.Text;

namespace MotsSupplierPortal.Application.Audit;

/// <summary>
/// MSP-75/FR-AUD-004: CSV formatting for one exported audit row, kept separate from the endpoint so
/// it is unit-testable without a database. RFC 4180 quoting - a field is quoted only when it
/// contains a comma, quote, or newline, and an embedded quote is doubled. ActorLabel is free text
/// (a name, MSP-62) and is the one column that realistically needs this; the others are
/// system-controlled identifiers/enums, but every column is escaped the same way rather than
/// trusting that to stay true.
/// </summary>
public static class AuditCsvRow
{
    public static string Format(AuditLogEntryDto entry) => string.Join(',', [
        Escape(entry.Id.ToString()),
        Escape(entry.OccurredAt.ToString("O", CultureInfo.InvariantCulture)),
        Escape(entry.AggregateType),
        Escape(entry.AggregateId.ToString()),
        Escape(entry.Action),
        Escape(entry.FromState),
        Escape(entry.ToState),
        Escape(entry.ActorLabel),
    ]);

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            if (c == '"') sb.Append('"');
            sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }
}
