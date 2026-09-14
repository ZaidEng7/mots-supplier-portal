// One titled table in an exported file: a heading, column headings, and rows of cells.
//
// It lives in this layer rather than beside the renderer that draws it. It began beside the renderer, and
// the moment a second consumer needed to build one, the report views that decide what a row says and in
// which numerals, the layer rule refused it, because this layer may not depend on that one.
//
// That refusal was right rather than an obstacle. What a section contains is a decision about the report;
// how it is drawn is a decision about the renderer. Only the second belongs to the renderer.
//
// Cells are already-formatted text. The renderer cannot tell a count from a duration and must not have
// to: the choice of numerals, the marker for something not measured, and every other content decision
// belong to the layer that knows what the number means.

namespace MotsSupplierPortal.Application.Exports;

public sealed record ReportSection(
    string Heading,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows);
