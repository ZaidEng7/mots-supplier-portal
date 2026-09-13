// One exported audit row, as a line of a spreadsheet file.
//
// It is separate from the route so it can be tested without a database.
//
// The quoting follows the spreadsheet standard: a field is quoted only when it contains a comma, a quote
// or a line break, and an embedded quote is doubled.
//
// The actor's label is free text, a person's name, and is the one column that realistically needs that.
// The others are identifiers and fixed values controlled by the system. Every column is escaped the same
// way regardless, rather than trusting that to stay true.
//
// The byte-order mark is the export engine's, re-exposed here so existing callers keep one name for it.

namespace MotsSupplierPortal.Application.Audit;

using System.Globalization;
using System.Text;
using MotsSupplierPortal.Application.Exports;

public static class AuditCsvRow
{
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
