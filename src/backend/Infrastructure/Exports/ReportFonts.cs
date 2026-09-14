// The two faces every generated document is drawn with, and the rule for choosing between them.
//
//
// WHY THERE ARE TWO
//
// The spike found that the Arabic face contains no Latin glyphs at all. A reference code shaped against it produced
// fifteen glyphs of which five were the undefined one, the empty box.
//
// Every artefact this engine produces carries reference codes, and in the Arabic exports those sit inside Arabic
// sentences, so a single face cannot render a single line of this product's output.
//
// The fallback is not a refinement. Without it the feature does not work.
//
//
// WHY THEY ARE EMBEDDED
//
// Resolved from the host, a document renders on a developer's machine, which ships Arabic system fonts, and shows
// boxes on a reviewer's machine or in the deployment container, which has no fonts installed at all.
//
// Embedded, the same bytes shape the text everywhere. Both faces are under a licence that permits this, and its
// text ships beside them.
//
// They are loaded once. A face and a font are native handles, and re-creating them per row would dominate the cost
// of an export that has thousands.

namespace MotsSupplierPortal.Infrastructure.Exports;

using System.Reflection;
using HarfBuzzSharp;
using SkiaSharp;

public sealed class ReportFonts : IDisposable
{
    public FontFace Arabic { get; }

    public FontFace Latin { get; }

    public ReportFonts()
    {
        Arabic = FontFace.FromEmbedded("MotsSupplierPortal.Infrastructure.Exports.Fonts.NotoNaskhArabic-Regular.ttf");
        Latin = FontFace.FromEmbedded("MotsSupplierPortal.Infrastructure.Exports.Fonts.NotoSans-Regular.ttf");
    }

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

public sealed class FontFace : IDisposable
{
    public const int Scale = 512;

    private readonly Blob _blob;
    private readonly Face _face;

    public string Name { get; }
    public Font Font { get; }
    public SKTypeface Typeface { get; }

    private FontFace(string name, byte[] bytes)
    {
        Name = name;

        var ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
        _blob = new Blob(ptr, bytes.Length, MemoryMode.ReadOnly,
            () => System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr));

        _face = new Face(_blob, 0);
        Font = new Font(_face);
        Font.SetScale(Scale, Scale);

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

    public bool HasGlyphFor(int codepoint) => Typeface.GetGlyph(codepoint) != 0;

    public void Dispose()
    {
        Typeface.Dispose();
        Font.Dispose();
        _face.Dispose();
        _blob.Dispose();
    }
}
