namespace MotsSupplierPortal.Application.Reporting;

/// <summary>
/// One titled table in a report artefact: a heading, column headers, and rows of already-formatted
/// cells.
///
/// <para><b>Why this lives in Application and not beside the PDF writer.</b> It started as
/// <c>PdfSection</c> in Infrastructure, and the moment a second consumer needed to BUILD one - the
/// report views, which decide what a row says and in which numerals - the layer rule refused it:
/// Application may not depend on Infrastructure. That refusal was right and not an obstacle. What a
/// section contains is a decision about the report; how it is drawn is a decision about the
/// renderer. Only the second is Infrastructure's.</para>
///
/// <para>Cells are strings, already formatted by the view. The renderer does not know a count from a
/// duration and must not: R-1's numeral choice, the "(not measured)" marker and every other
/// content decision belong to the layer that knows what the number means.</para>
/// </summary>
public sealed record ReportSection(
    string Heading,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows);
