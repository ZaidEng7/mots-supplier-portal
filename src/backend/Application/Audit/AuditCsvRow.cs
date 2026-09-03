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
    /// <summary>The engine's BOM, re-exposed so existing call sites keep one name for it.</summary>
    public static readonly byte[] Utf8Bom = CsvFormat.Utf8Bom;

    public static string Format(AuditLogEntryDto entry) => CsvFormat.Row([
        entry.Id.ToString(),
        entry.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
        entry.AggregateType,
        entry.AggregateId.ToString(),
        entry.Action,
        entry.FromState,
        entry.ToState,
        entry.ActorLabel,
    ]);
}
