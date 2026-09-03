using System.Globalization;

namespace MotsSupplierPortal.Application.Reporting;

/// <summary>
/// What an exported artefact says about itself: when it was produced, under whose scope, and every
/// filter that shaped it.
///
/// <para><b>Why it belongs to the engine and not to one export.</b> Audit rows, comparison sheets and
/// report tables are all records of a tender, and once a file is detached from the request that
/// produced it, one with a truncated range is indistinguishable from a complete one. That is exactly
/// the state it is in when someone attaches it to a dispute. Stating the filters inside the artefact
/// turns "this is everything between these dates" into a claim the file makes and a reader can
/// check. Every artefact this engine produces carries one, so it is defined here rather than
/// re-derived per consumer.</para>
///
/// <para>An absent filter is rendered as "(unbounded)" or "(all)" rather than omitted. A missing
/// line reads as a missing filter, which is the ambiguity this exists to remove.</para>
/// </summary>
/// <param name="GeneratedAt">When the artefact was produced.</param>
/// <param name="Scope">The row-scope the request ran under, in words - "all organizations
/// (audit.read)", "one supplier's own trail". A file that does not say this cannot be checked
/// against what the reader was entitled to see.</param>
/// <param name="Filters">Every filter the endpoint accepts, present or not.</param>
public sealed record ExportProvenance(
    DateTimeOffset GeneratedAt,
    string Scope,
    IReadOnlyList<ExportFilterValue> Filters)
{
    public const string Product = "MOTS Supplier Portal";

    /// <summary>
    /// The block as CSV comment lines.
    ///
    /// <para>Comment lines prefixed with <c>#</c> are a CONVENTION, not RFC 4180 - the standard has
    /// no comment syntax. Spreadsheets import them as single-column rows above the table. A separate
    /// manifest file was the alternative and loses the connection the moment someone forwards one
    /// attachment and not the other.</para>
    /// </summary>
    public IEnumerable<string> ToCsvComments(string artefactName)
    {
        yield return $"# {Product} - {artefactName}";
        yield return $"# generated: {GeneratedAt.ToString("O", CultureInfo.InvariantCulture)}";
        yield return $"# scope: {Scope}";

        foreach (var filter in Filters)
        {
            yield return $"# filter.{filter.Key}: {filter.Display}";
        }
    }

    /// <summary>
    /// The block as lines for a rendered page.
    ///
    /// <para>The <c>#</c> is deliberately NOT carried over. It is a CSV comment marker with no
    /// meaning in a PDF, and looking at the first rendered artefact showed why that matters: as a
    /// neutral character at the start of an RTL line it takes the paragraph direction and is drawn
    /// at the far RIGHT of the line, so <c>"# scope: ..."</c> rendered as <c>"scope: ... #"</c>.
    /// Correct bidi, meaningless output. A machine-facing marker in a human-facing artefact.</para>
    /// </summary>
    public IEnumerable<string> ToDisplayLines(string artefactName)
    {
        yield return $"{Product} — {artefactName}";
        yield return $"generated: {GeneratedAt.ToString("O", CultureInfo.InvariantCulture)}";
        yield return $"scope: {Scope}";

        foreach (var filter in Filters)
        {
            yield return $"{filter.Key}: {filter.Display}";
        }
    }
}

/// <summary>One filter and the value it ran with, including the case where it ran with none.</summary>
/// <param name="Key">The query parameter's name, so the line can be matched back to a request.</param>
/// <param name="Display">The value, or the explicit absence marker.</param>
public sealed record ExportFilterValue(string Key, string Display)
{
    public static ExportFilterValue Optional(string key, string? value) =>
        new(key, string.IsNullOrWhiteSpace(value) ? "(all)" : value);

    public static ExportFilterValue Bound(string key, DateTimeOffset? value) =>
        new(key, value?.ToString("O", CultureInfo.InvariantCulture) ?? "(unbounded)");

    /// <summary>
    /// Distinctly named rather than an overload: <c>Optional(key, null)</c> is ambiguous between a
    /// null string and a null Guid, and the compiler is right that it is - the two say the same
    /// thing but only one of them compiles.
    /// </summary>
    public static ExportFilterValue OptionalId(string key, Guid? value) =>
        new(key, value?.ToString() ?? "(all)");
}
