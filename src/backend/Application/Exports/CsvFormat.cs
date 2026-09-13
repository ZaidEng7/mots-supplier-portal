// Spreadsheet formatting, shared by every such file this product produces.
//
// A field is quoted only when it contains a comma, a quote or a line break, and an embedded quote is
// doubled. The byte-order mark goes first, because an Arabic export without it is silently unreadable in
// the tool most people open it with.
//
// It was lifted out of the audit export the moment a second consumer appeared. Quoting rules and a
// byte-order mark are properties of any spreadsheet this product emits rather than of one route, and two
// copies would eventually be two behaviours. The one that would drift is the escaping, which is
// invisible until a supplier's name contains a comma.

namespace MotsSupplierPortal.Application.Exports;

using System.Text;

public static class CsvFormat
{
    public static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static string Row(IEnumerable<string?> fields) => string.Join(',', fields.Select(Escape));

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0) return value;

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var c in value)
        {
            if (c == '"') builder.Append('"');
            builder.Append(c);
        }
        builder.Append('"');
        return builder.ToString();
    }
}
