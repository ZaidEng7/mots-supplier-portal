// Shaping a line of mixed Arabic and Latin text into positioned glyphs.
//
// The shaping is the text-shaping library's rather than the renderer's. Arabic needs contextual substitution of
// initial, medial and final forms from the font's own substitution table, and a renderer that maps characters to
// glyphs one at a time produces disconnected letters that are readable to nobody.
//
// Verified during the spike against the shaping library's own reference tool: identical glyph identifiers, with
// joining-form names.
//
// The script and language are guessed from the content rather than assumed from the paragraph, because a Latin run
// inside an Arabic line must be shaped as Latin or it picks up Arabic shaping rules that do not apply to it.
//
// A line is drawn as positioned GLYPHS and never as a string. Handing the text back to the renderer would throw
// away the shaping and re-map characters to glyphs without the joining forms, which is the one thing this whole
// path exists to avoid.
//
//
// THE CHEAP CHECK THAT CATCHES THE FAILURE THE SPIKE FOUND
//
// A shaped line reports whether any glyph came out as the undefined one, the empty box.
//
// Text can be PRESENT in a document's content stream and render as a row of empty boxes. Asserting the string is in
// the file proves nothing about that; asserting no glyph is the undefined one does.

namespace MotsSupplierPortal.Infrastructure.Exports;

using HarfBuzzSharp;
using SkiaSharp;
using Buffer = HarfBuzzSharp.Buffer;

public sealed record PlacedGlyph(ushort GlyphId, float X, float Y, FontFace Face);

public sealed record ShapedLine(IReadOnlyList<PlacedGlyph> Glyphs, float Width)
{
    public bool HasMissingGlyphs => Glyphs.Any(g => g.GlyphId == 0);
}

public sealed class TextShaper(ReportFonts fonts)
{
    public ShapedLine Shape(string text, RunDirection paragraph, float sizeInPoints)
    {
        var glyphs = new List<PlacedGlyph>();
        var x = 0f;

        foreach (var run in BidiRuns.Resolve(text, paragraph, fonts))
        {
            using var buffer = new Buffer();
            buffer.AddUtf16(run.Text);
            buffer.Direction = run.Direction == RunDirection.RightToLeft ? Direction.RightToLeft : Direction.LeftToRight;

            buffer.GuessSegmentProperties();

            run.Face.Font.Shape(buffer);

            var infos = buffer.GlyphInfos;
            var positions = buffer.GlyphPositions;
            var factor = sizeInPoints / FontFace.Scale;

            for (var i = 0; i < infos.Length; i++)
            {
                glyphs.Add(new PlacedGlyph(
                    (ushort)infos[i].Codepoint,
                    x + positions[i].XOffset * factor,
                    -positions[i].YOffset * factor,
                    run.Face));

                x += positions[i].XAdvance * factor;
            }
        }

        return new ShapedLine(glyphs, x);
    }

    public static void Draw(SKCanvas canvas, ShapedLine line, float left, float baseline, float sizeInPoints, SKColor colour)
    {
        using var paint = new SKPaint { Color = colour, IsAntialias = true };

        foreach (var group in line.Glyphs.GroupBy(g => g.Face))
        {
            using var font = new SKFont(group.Key.Typeface, sizeInPoints);
            var placed = group.ToArray();

            using var builder = new SKTextBlobBuilder();
            var run = builder.AllocatePositionedRun(font, placed.Length);
            var ids = run.Glyphs;
            var points = run.Positions;

            for (var i = 0; i < placed.Length; i++)
            {
                ids[i] = placed[i].GlyphId;
                points[i] = new SKPoint(left + placed[i].X, baseline + placed[i].Y);
            }

            using var blob = builder.Build();
            if (blob is not null) canvas.DrawText(blob, 0, 0, paint);
        }
    }
}
