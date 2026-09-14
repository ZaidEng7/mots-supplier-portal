// The stop condition for the document engine: it does not exist until a reference code renders.
//
// The spike found that the Arabic face has no Latin glyphs, so a reference code shaped against it produced five
// undefined boxes out of fifteen glyphs.
//
// These tests exist because "the string is in the document" is not evidence of anything. A content stream full
// of the undefined glyph contains the text and renders as empty boxes.
//
// The sample is an Arabic sentence carrying a Latin reference code and an Eastern Arabic numeral: the exact
// mixture every artefact in this feature contains, and the one no single font can render.
//
//
// EACH ASSERTION HAS THE CONTROL IT NEEDS
//
// Without the fallback control, the first assertion passes on any string a single font happens to cover and
// proves nothing about fallback. That control is the state the engine was in before fallback: the Arabic face
// alone, asked for Latin, answering with boxes.
//
// The joining-forms assertion is the property that distinguishes shaped Arabic from a character-by-character
// mapping. If it fails, the text is present, legible to a machine, and unreadable to a person.
//
//
// THE ORDERING IS ASSERTED IN BOTH DIRECTIONS, BECAUSE ONE DIRECTION WAS NEAR-VACUOUS
//
// The first version measured the widest Arabic glyph in a long sentence against the code's position, and passed
// under left-to-right too, because a sentence with Arabic on both sides of the code has Arabic at the far right
// either way.
//
// So the sample is two runs only, which makes the ordering the only thing the numbers can be about, and the same
// string is asserted under both paragraph directions. Without the second, the first is satisfied by an engine
// that ignores direction entirely.
//
// In both, the code's own letters advance left to right, because a Latin run keeps its own direction inside a
// right-to-left line, and that is the property that makes a reference code readable.
//
// And quantities and currency use Arabic-Indic digits under Arabic, where a number reads left to right even
// inside right-to-left text, which is a property of the ordering algorithm rather than of the font.

namespace MotsSupplierPortal.Tests.Unit.Exports;

using FluentAssertions;
using MotsSupplierPortal.Application.Exports;
using MotsSupplierPortal.Infrastructure.Exports;

public sealed class ArabicPdfRenderingTests
{
    private const string Sentence = "طلب عرض أسعار RFQ-2026-000001 بقيمة ٢٥٠٠٠ ليرة";

    [Fact]
    public void No_glyph_in_a_mixed_Arabic_and_Latin_line_is_notdef()
    {
        using var fonts = new ReportFonts();
        var shaper = new TextShaper(fonts);

        var line = shaper.Shape(Sentence, RunDirection.RightToLeft, 12f);

        line.Glyphs.Should().NotBeEmpty();
        line.HasMissingGlyphs.Should().BeFalse(
            "a .notdef is an empty box on the page - the failure the spike found, and one that " +
            "an assertion about the string being present cannot see");
    }

    [Fact]
    public void The_control_a_single_face_cannot_render_the_same_line()
    {
        using var fonts = new ReportFonts();

        var missing = "RFQ-2026-000001".Where(c => !fonts.Arabic.HasGlyphFor(c)).ToList();

        missing.Should().NotBeEmpty(
            "control: the Arabic face genuinely cannot draw a reference code, so the test above is " +
            "about the fallback and not about a font that covered everything anyway");
    }

    [Fact]
    public void Both_faces_are_used_for_one_line()
    {
        using var fonts = new ReportFonts();
        var shaper = new TextShaper(fonts);

        var faces = shaper.Shape(Sentence, RunDirection.RightToLeft, 12f)
            .Glyphs.Select(g => g.Face).Distinct().ToList();

        faces.Should().HaveCount(2, "the line is split across the two faces, not forced into one");
        faces.Should().Contain(fonts.Arabic).And.Contain(fonts.Latin);
    }

    [Fact]
    public void Arabic_letters_are_shaped_into_joining_forms_not_isolated_glyphs()
    {
        using var fonts = new ReportFonts();
        var shaper = new TextShaper(fonts);

        var word = "مرحبا";
        var shaped = shaper.Shape(word, RunDirection.RightToLeft, 12f);

        var isolated = word.Select(c => fonts.Arabic.Typeface.GetGlyph(c)).ToList();
        var rendered = shaped.Glyphs.Select(g => g.GlyphId).ToList();

        rendered.Should().NotBeEquivalentTo(isolated,
            "shaping replaced the isolated forms with contextual ones; identical ids would mean " +
            "GSUB never ran and the letters are drawn unjoined");
    }

    [Fact]
    public void A_Latin_code_inside_Arabic_is_placed_by_the_paragraph_direction_not_by_logical_order()
    {
        using var fonts = new ReportFonts();
        var shaper = new TextShaper(fonts);
        const string twoRuns = "مرحبا RFQ";

        var rtl = shaper.Shape(twoRuns, RunDirection.RightToLeft, 12f);
        var ltr = shaper.Shape(twoRuns, RunDirection.LeftToRight, 12f);

        float LatinX(ShapedLine line, char c) =>
            line.Glyphs.First(g => ReferenceEquals(g.Face, fonts.Latin)
                                && g.GlyphId == fonts.Latin.Typeface.GetGlyph(c)).X;
        float ArabicMaxX(ShapedLine line) =>
            line.Glyphs.Where(g => ReferenceEquals(g.Face, fonts.Arabic)).Max(g => g.X);

        ArabicMaxX(rtl).Should().BeGreaterThan(LatinX(rtl, 'Q'),
            "on an RTL line the sentence opens on the right, so the code sits left of the Arabic");

        ArabicMaxX(ltr).Should().BeLessThan(LatinX(ltr, 'R'),
            "on an LTR line the Arabic comes first on the LEFT and the code follows it");

        LatinX(rtl, 'R').Should().BeLessThan(LatinX(rtl, 'Q'));
        LatinX(ltr, 'R').Should().BeLessThan(LatinX(ltr, 'Q'));
    }

    [Fact]
    public void Eastern_Arabic_digits_render_and_read_left_to_right()
    {
        using var fonts = new ReportFonts();
        var shaper = new TextShaper(fonts);

        var line = shaper.Shape("المبلغ ١٢٣ ليرة", RunDirection.RightToLeft, 12f);
        line.HasMissingGlyphs.Should().BeFalse("the Arabic face carries Arabic-Indic digits");

        float XOf(char c) => line.Glyphs.First(g => g.GlyphId == fonts.Arabic.Typeface.GetGlyph(c)).X;

        XOf('١').Should().BeLessThan(XOf('٢'), "one before two, left to right");
        XOf('٢').Should().BeLessThan(XOf('٣'), "and two before three");
    }

    [Fact]
    public void A_rendered_pdf_is_a_real_pdf_and_carries_both_embedded_faces()
    {
        using var fonts = new ReportFonts();
        var writer = new PdfReportWriter(fonts);

        using var stream = new MemoryStream();
        writer.Write(stream, new PdfReportSpec(
            RunDirection.RightToLeft,
            "تقرير طلبات عروض الأسعار",
            "RFQ report",
            new ExportProvenance(DateTimeOffset.UnixEpoch, "منظمة واحدة",
                [ExportFilterValue.Bound("from", null)]),
            [new ReportSection("الطلبات", ["المرجع", "الحالة", "القيمة"],
                [["RFQ-2026-000001", "منشور", "٢٥٠٠٠"]])]));

        var bytes = stream.ToArray();

        bytes.Should().StartWith("%PDF"u8.ToArray(), "it has to actually be a PDF");
        bytes.Length.Should().BeGreaterThan(20_000,
            "an embedded font subset is tens of kilobytes; a few hundred bytes would mean the text " +
            "was drawn with no font embedded at all, which renders on this machine and nowhere else");

        var text = System.Text.Encoding.Latin1.GetString(bytes);
        text.Should().Contain("FontFile2", "the faces are embedded in the file, not referenced by name");
        text.Should().Contain("NotoNaskhArabic").And.Contain("NotoSans",
            "both faces - one alone cannot draw this page");
    }
}
