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
    /// <summary>
    /// The provenance block an exported audit file carries above its header row.
    ///
    /// <para><b>Why the file has to say this itself.</b> Audit rows are the record of a tender, and a
    /// CSV with a silently truncated range is indistinguishable from a complete one once it is
    /// detached from the request that produced it - which is exactly what happens when it is attached
    /// to a dispute. Stating the filters inside the artefact makes "this is everything between these
    /// dates" a claim the file makes and a reader can check, rather than an assumption.</para>
    ///
    /// <para>Comment lines are prefixed with <c>#</c>. RFC 4180 has no comment syntax, so this is a
    /// CONVENTION, not a standard: spreadsheet applications import these as single-column rows above
    /// the table. The alternative - a separate manifest file - loses the connection the moment
    /// someone forwards one attachment and not the other.</para>
    /// </summary>
    public static IEnumerable<string> ProvenanceHeader(
        DateTimeOffset generatedAt,
        string? aggregateType, string? action,
        DateTimeOffset? from, DateTimeOffset? to,
        string scopeDescription)
    {
        yield return $"# MOTS Supplier Portal - audit export";
        yield return $"# generated: {generatedAt.ToString("O", CultureInfo.InvariantCulture)}";
        yield return $"# scope: {Escape(scopeDescription)}";
        yield return $"# filter.aggregateType: {aggregateType ?? "(all)"}";
        yield return $"# filter.action: {action ?? "(all)"}";
        yield return $"# filter.from: {from?.ToString("O", CultureInfo.InvariantCulture) ?? "(unbounded)"}";
        yield return $"# filter.to: {to?.ToString("O", CultureInfo.InvariantCulture) ?? "(unbounded)"}";
    }

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
