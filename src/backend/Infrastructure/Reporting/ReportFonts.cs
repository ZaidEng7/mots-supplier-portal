using System.Reflection;
using HarfBuzzSharp;
using SkiaSharp;

namespace MotsSupplierPortal.Infrastructure.Reporting;

/// <summary>
/// The two faces every generated PDF is drawn with, and the rule for choosing between them.
///
/// <para><b>Why there are two.</b> EPIC-19's spike found that Noto Naskh Arabic contains no Latin
/// glyphs at all: <c>RFQ-2026-000001</c> shaped against it produced fifteen glyphs of which five
/// were <c>.notdef</c> - the empty box. Every artefact this engine produces carries reference codes,
/// and in the Arabic exports those sit inside Arabic sentences, so a single face cannot render a
/// single line of this product's output. The fallback is not a refinement; without it the feature
/// does not work.</para>
///
/// <para><b>Why they are embedded.</b> Resolved from the host, a PDF renders on a developer's Mac
/// (which ships Arabic system fonts) and shows boxes on a reviewer's machine or in the deployment
/// container, which has no fonts installed at all. Embedded, the same bytes shape the text
/// everywhere. Both are SIL Open Font License 1.1, which permits this; OFL.txt ships beside them.
/// </para>
///
/// <para>Loaded once. A Face and a Font are native handles and re-creating them per row would
/// dominate the cost of an export that has thousands.</para>
/// </summary>
public sealed class ReportFonts : IDisposable
{
    /// <summary>Arabic, and the Arabic-Indic digits R-1 requires under the Arabic locale.</summary>
    public FontFace Arabic { get; }

    /// <summary>Latin letters, ASCII digits and punctuation - reference codes, ISO dates, headers.</summary>
    public FontFace Latin { get; }

    public ReportFonts()
    {
        Arabic = FontFace.FromEmbedded("MotsSupplierPortal.Infrastructure.Reporting.Fonts.NotoNaskhArabic-Regular.ttf");
        Latin = FontFace.FromEmbedded("MotsSupplierPortal.Infrastructure.Reporting.Fonts.NotoSans-Regular.ttf");
    }

    /// <summary>
    /// The face that can actually draw this code point, or null if neither can.
    ///
    /// <para>Coverage is asked of the FONT rather than inferred from the character's Unicode block.
    /// A block test would have got this wrong in both directions: Noto Naskh does carry
    /// Arabic-Indic digits and common punctuation, and it does not carry Latin, which is not
    /// something the block of a character tells you.</para>
    /// </summary>
    public FontFace? ResolveFor(int codepoint)
    {
        if (Arabic.HasGlyphFor(codepoint)) return Arabic;
        if (Latin.HasGlyphFor(codepoint)) return Latin;
        return null;
    }

    public void Dispose()
    {
        Arabic.Dispose();
        Latin.Dispose();
    }
}

/// <summary>One embedded face, held as both a HarfBuzz font (shaping) and an SKTypeface (drawing).</summary>
public sealed class FontFace : IDisposable
{
    /// <summary>
    /// HarfBuzz shapes in font units scaled by this factor; Skia positions in points. Shaping at a
    /// fixed scale and dividing keeps the two in one coordinate system without re-shaping per size.
    /// </summary>
    public const int Scale = 512;

    private readonly Blob _blob;
    private readonly Face _face;

    public string Name { get; }
    public Font Font { get; }
    public SKTypeface Typeface { get; }

    private FontFace(string name, byte[] bytes)
    {
        Name = name;

        // Copied into unmanaged memory rather than pinned: the Blob outlives this constructor and a
        // GC that moved the array under it would corrupt shaping in a way that looks like a font bug.
        var ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
        _blob = new Blob(ptr, bytes.Length, MemoryMode.ReadOnly,
            () => System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr));

        _face = new Face(_blob, 0);
        Font = new Font(_face);
        Font.SetScale(Scale, Scale);

        // OpenType functions, not the fallback set: contextual Arabic shaping (.init/.medi/.fina)
        // comes from GSUB, and without these HarfBuzz falls back to unjoined letters - the exact
        // failure this engine was verified against.
        Font.SetFunctionsOpenType();

        Typeface = SKTypeface.FromData(SKData.CreateCopy(bytes))
            ?? throw new InvalidOperationException($"Skia could not read the embedded font '{name}'.");
    }

    public static FontFace FromEmbedded(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded font '{resourceName}' is missing. Exports cannot render without it; " +
                "check the EmbeddedResource item in the Infrastructure project.");

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return new FontFace(resourceName, memory.ToArray());
    }

    /// <summary>Whether this face has a real glyph for the code point - glyph 0 is .notdef.</summary>
    public bool HasGlyphFor(int codepoint) => Typeface.GetGlyph(codepoint) != 0;

    public void Dispose()
    {
        Typeface.Dispose();
        Font.Dispose();
        _face.Dispose();
        _blob.Dispose();
    }
}
