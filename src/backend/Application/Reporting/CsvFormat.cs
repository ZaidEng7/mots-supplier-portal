using System.Text;

namespace MotsSupplierPortal.Application.Reporting;

/// <summary>
/// RFC 4180 formatting, shared by every CSV this engine produces.
///
/// <para>Lifted out of the audit export when a second consumer appeared: quoting rules and a BOM are
/// properties of "a CSV this product emits", not of one endpoint. Two copies would eventually be two
/// behaviours, and the one that would drift is the escaping - which is invisible until a supplier
/// name contains a comma.</para>
/// </summary>
public static class CsvFormat
{
    /// <summary>
    /// UTF-8 BOM.
    ///
    /// <para>Excel on Windows reads a BOM-less UTF-8 CSV as the system code page, which turns every
    /// Arabic name in an exported file into mojibake - and the file still opens, so nothing signals
    /// the loss. Three bytes between a readable governance artefact and one that has to be
    /// re-exported by someone who knows the trick. Asserted on the BYTES: a string comparison passes
    /// without it.</para>
    ///
    /// <para>RTL inside a cell is the consumer's business. A CSV carries no direction information at
    /// all, and injecting bidi control characters to force it would corrupt the data for every
    /// non-spreadsheet reader.</para>
    /// </summary>
    public static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static string Row(IEnumerable<string?> fields) => string.Join(',', fields.Select(Escape));

    /// <summary>
    /// A field is quoted only when it contains a comma, a quote, or a newline; an embedded quote is
    /// doubled. Free text is the column that realistically needs this, but every column is escaped
    /// the same way rather than trusting the others to stay machine-controlled.
    /// </summary>
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
