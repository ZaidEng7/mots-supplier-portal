// Rendering a report to a document with both faces embedded.
//
// Deliberately plain. This is not a layout engine: it draws a title, a provenance block and sectioned tables, and
// paginates when it runs out of page. Everything this feature's consumers need is that shape, and a richer one
// would be speculative.
//
// The direction is right-to-left for the Arabic artefacts and left-to-right for the English ones, and it drives
// both the bidirectional resolution and which edge a line is aligned to. A right-to-left report aligned left is not
// merely ugly; it puts every line's start where a reader is not looking.
//
// A table's columns are laid out from the reading edge inward, so a right-to-left table's first column is its
// rightmost, which is the same order the interface's own right-to-left tables use.
//
// The provenance block names when the artefact was generated, under whose scope, and every filter that shaped it.
// It is the SAME model the spreadsheet exports carry, rendered for a page rather than for a spreadsheet's comment
// rows.
//
// The page is measured in the units the document library works in.

namespace MotsSupplierPortal.Infrastructure.Exports;

using MotsSupplierPortal.Application.Exports;
using SkiaSharp;

public sealed record PdfReportSpec(
    RunDirection Direction,
    string Title,
    string ArtefactName,
    ExportProvenance Provenance,
    IReadOnlyList<ReportSection> Sections);

public sealed class PdfReportWriter(ReportFonts fonts)
{
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
