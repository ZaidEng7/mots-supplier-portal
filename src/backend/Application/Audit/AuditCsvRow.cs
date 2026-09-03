using System.Globalization;
using System.Text;
using MotsSupplierPortal.Application.Reporting;

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
    /// <summary>
    /// UTF-8 BOM.
    ///
    /// <para>Excel on Windows reads a BOM-less UTF-8 CSV as the system code page, which turns every
    /// Arabic name in an exported file into mojibake - and the file still opens, so nothing signals
    /// the loss. The BOM is three bytes that make the difference between a readable governance
    /// artefact and one that has to be re-exported by someone who knows the trick.</para>
    ///
    /// <para>RTL inside a cell is the consumer's business: a CSV carries no direction information at
    /// all, and injecting bidi control characters to force it would corrupt the data for every
    /// non-spreadsheet reader.</para>
    /// </summary>
    public static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

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
