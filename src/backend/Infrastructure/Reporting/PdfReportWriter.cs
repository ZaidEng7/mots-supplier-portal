using MotsSupplierPortal.Application.Reporting;
using SkiaSharp;

namespace MotsSupplierPortal.Infrastructure.Reporting;

/// <summary>
/// Where a report's text sits on the page, and which way it reads.
/// </summary>
/// <param name="Direction">RTL for the Arabic artefacts, LTR for the English ones. Drives both the
/// bidi resolution and which edge a line is aligned to - an RTL report aligned left is not merely
/// ugly, it puts every line's start where a reader is not looking.</param>
/// <param name="Title">The document title, drawn at the top of the first page.</param>
/// <param name="Provenance">The block naming when this artefact was generated, under whose scope,
/// and every filter that shaped it - the SAME model the CSV exports carry, rendered for a page
/// rather than for a spreadsheet's comment rows.</param>
/// <param name="ArtefactName">What this file is, in one phrase, for the provenance block's first line.</param>
public sealed record PdfReportSpec(
    RunDirection Direction,
    string Title,
    string ArtefactName,
    ExportProvenance Provenance,
    IReadOnlyList<ReportSection> Sections);

/// <summary>
/// FEAT-19.4: renders a report to PDF/A-shaped output with both faces embedded.
///
/// <para>Deliberately plain. This is not a layout engine: it draws a title, a provenance block, and
/// sectioned tables, paginating when it runs out of page. Everything this epic's consumers need is
/// that shape, and a richer one would be speculative.</para>
/// </summary>
public sealed class PdfReportWriter(ReportFonts fonts)
{
    // A4 at 72dpi, which is the unit SKDocument.CreatePdf works in.
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Margin = 48f;

    private const float TitleSize = 16f;
    private const float HeadingSize = 12f;
    private const float BodySize = 10f;
    private const float ProvenanceSize = 8f;

    private static readonly SKColor Ink = new(0x1A, 0x1A, 0x1A);
    private static readonly SKColor Muted = new(0x66, 0x66, 0x66);
    private static readonly SKColor Rule = new(0xCC, 0xCC, 0xCC);

    private readonly TextShaper _shaper = new(fonts);

    public void Write(Stream destination, PdfReportSpec spec)
    {
        using var document = SKDocument.CreatePdf(destination, new SKDocumentPdfMetadata
        {
            Title = spec.Title,
            Producer = "MOTS Supplier Portal",
            Creation = DateTime.UtcNow,
        });

        var page = new PageCursor(document, spec.Direction);

        page.Line(_shaper, spec.Title, TitleSize, Ink);
        page.Gap(6f);

        foreach (var line in spec.Provenance.ToDisplayLines(spec.ArtefactName))
        {
            page.Line(_shaper, line, ProvenanceSize, Muted);
        }

        page.Gap(10f);
        page.HorizontalRule();

        foreach (var section in spec.Sections)
        {
            page.Gap(12f);
            page.Line(_shaper, section.Heading, HeadingSize, Ink);
            page.Gap(4f);

            page.Row(_shaper, section.Columns, BodySize, Muted);
            page.HorizontalRule();

            foreach (var row in section.Rows)
            {
                page.Row(_shaper, row, BodySize, Ink);
            }
        }

        page.Finish();
    }

    /// <summary>
    /// Tracks the current page and vertical position, starting a new page when a line will not fit.
    /// Alignment follows the report's direction: an RTL report's lines start at the right margin.
    /// </summary>
    private sealed class PageCursor(SKDocument document, RunDirection direction)
    {
        private SKCanvas? _canvas;
        private float _y;

        private SKCanvas Canvas
        {
            get
            {
                if (_canvas is null)
                {
                    _canvas = document.BeginPage(PageWidth, PageHeight);
                    _y = Margin;
                }
                return _canvas;
            }
        }

        public void Gap(float amount) => _y += amount;

        public void Line(TextShaper shaper, string text, float size, SKColor colour)
        {
            var shaped = shaper.Shape(text, direction, size);
            EnsureRoom(size * 1.6f);

            var left = direction == RunDirection.RightToLeft
                ? PageWidth - Margin - shaped.Width
                : Margin;

            TextShaper.Draw(Canvas, shaped, left, _y + size, size, colour);
            _y += size * 1.6f;
        }

        /// <summary>
        /// One table row. Columns are laid out from the reading edge inward, so an RTL table's first
        /// column is its rightmost - the same order the SPA's RTL tables use.
        /// </summary>
        public void Row(TextShaper shaper, IReadOnlyList<string> cells, float size, SKColor colour)
        {
            if (cells.Count == 0) return;

            EnsureRoom(size * 1.7f);

            var usable = PageWidth - (Margin * 2);
            var columnWidth = usable / cells.Count;

            for (var i = 0; i < cells.Count; i++)
            {
                var shaped = shaper.Shape(cells[i], direction, size);

                var columnStart = direction == RunDirection.RightToLeft
                    ? PageWidth - Margin - ((i + 1) * columnWidth)
                    : Margin + (i * columnWidth);

                var left = direction == RunDirection.RightToLeft
                    ? columnStart + columnWidth - Math.Min(shaped.Width, columnWidth)
                    : columnStart;

                TextShaper.Draw(Canvas, shaped, left, _y + size, size, colour);
            }

            _y += size * 1.7f;
        }

        public void HorizontalRule()
        {
            EnsureRoom(6f);
            using var paint = new SKPaint { Color = Rule, StrokeWidth = 0.5f };
            Canvas.DrawLine(Margin, _y, PageWidth - Margin, _y, paint);
            _y += 6f;
        }

        private void EnsureRoom(float needed)
        {
            _ = Canvas;
            if (_y + needed <= PageHeight - Margin) return;

            document.EndPage();
            _canvas = null;
            _ = Canvas;
        }

        public void Finish()
        {
            if (_canvas is not null) document.EndPage();
            document.Close();
        }
    }
}
