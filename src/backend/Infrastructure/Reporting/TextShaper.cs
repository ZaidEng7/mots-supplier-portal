using HarfBuzzSharp;
using SkiaSharp;
using Buffer = HarfBuzzSharp.Buffer;

namespace MotsSupplierPortal.Infrastructure.Reporting;

/// <summary>One glyph, placed. X grows to the right regardless of the run's direction.</summary>
/// <param name="GlyphId">Index into <paramref name="Face"/>. 0 is .notdef - the empty box.</param>
public sealed record PlacedGlyph(ushort GlyphId, float X, float Y, FontFace Face);

/// <summary>A shaped line: its glyphs in visual order, and how wide it is.</summary>
public sealed record ShapedLine(IReadOnlyList<PlacedGlyph> Glyphs, float Width)
{
    /// <summary>
    /// Whether any glyph came out as .notdef.
    ///
    /// <para>This is the cheap mechanical check that catches the failure the EPIC-19 spike found:
    /// text that is PRESENT in the PDF's content stream and renders as a row of empty boxes.
    /// Asserting the string is in the file proves nothing about that; asserting no glyph is 0 does.
    /// </para>
    /// </summary>
    public bool HasMissingGlyphs => Glyphs.Any(g => g.GlyphId == 0);
}

/// <summary>
/// Shapes a line of mixed Arabic and Latin text into positioned glyphs.
///
/// <para>Shaping is HarfBuzz's, not Skia's: Arabic needs contextual substitution
/// (<c>.init</c>/<c>.medi</c>/<c>.fina</c>) from the font's GSUB table, and a renderer that maps
/// characters to glyphs one at a time produces disconnected letters that are readable to nobody.
/// Verified in the EPIC-19 spike against the reference <c>hb-shape</c> CLI: identical glyph ids,
/// with joining-form names.</para>
/// </summary>
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

            // Script and language are guessed from the content rather than assumed from the
            // paragraph: a Latin run inside an Arabic line must be shaped as Latin, or it picks up
            // Arabic shaping rules that do not apply to it.
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

    /// <summary>
    /// Draws a shaped line at (<paramref name="left"/>, <paramref name="baseline"/>).
    ///
    /// <para>Drawn as positioned GLYPHS, never as a string: handing the text back to Skia would
    /// throw away the shaping and re-map characters to glyphs without the joining forms, which is
    /// the one thing this whole path exists to avoid.</para>
    /// </summary>
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
